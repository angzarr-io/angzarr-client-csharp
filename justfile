# C# client library commands
#
# Container Overlay Pattern:
# --------------------------
# This justfile uses an overlay pattern for container execution:
#
# 1. `justfile` (this file) - runs on the host, delegates to container
# 2. `justfile.container` - mounted over this file inside the container
#
# When running outside a devcontainer:
#   - Uses pre-built angzarr-csharp image from ghcr.io/angzarr
#   - Docker mounts justfile.container as /workspace/justfile
#
# When running inside a devcontainer (DEVCONTAINER=true):
#   - Commands execute directly via `just <target>`
#   - No container nesting

set shell := ["bash", "-c"]

# Reusable submodule-protection recipes (install-submodule-hooks,
# check-submodules-clean). Source of truth: angzarr-project/submodule.just.
import? 'angzarr-project/submodule.just'

ROOT := `git rev-parse --show-toplevel`
IMAGE := "ghcr.io/angzarr-io/angzarr-csharp:latest"

# Run just target in container (or directly if already in devcontainer).
# Rootless docker: -u 0:0 maps to host user via subuid; writes to the
# bind-mount land owned by the host user. Rootful: direct uid match.
# See feedback_docker_rootless.
[private]
_container +ARGS:
    #!/usr/bin/env bash
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        just {{ARGS}}
    else
        if docker info --format '{{{{.SecurityOptions}}}}' 2>/dev/null | grep -q rootless; then
            USER_FLAG="-u 0:0"
        else
            USER_FLAG="-u $(id -u):$(id -g)"
        fi
        docker run --rm --network=host \
            $USER_FLAG \
            -v "{{ROOT}}:/workspace:Z" \
            -v "{{ROOT}}/justfile.container:/workspace/justfile:ro" \
            -w /workspace \
            -e DEVCONTAINER=true \
            {{IMAGE}} just {{ARGS}}
    fi

# Run a mutation-testing target with the workspace mounted READ-ONLY.
#
# WHY:
#   Stryker.NET writes mutated sandbox copies into StrykerOutput/. If the
#   workspace is bind-mounted RW (as `_container` does) and the container
#   dies mid-run, the mutated files are left on the host. This helper closes
#   that hole: source is mounted at /src:ro, an in-container tar copy lands
#   in /work (the container's WRITABLE OVERLAY LAYER), and `--rm` destroys
#   the overlay (and the mutated copies) on every exit.
#
# WHAT TOUCHES THE HOST:
#   - {{ROOT}}/.mutants-cache/{nuget,dotnet-tools} — NuGet package cache and
#     dotnet-tool restore output only. NEVER contains mutated source files.
#     Gitignored. Delete the dir to purge the cache.
#   - {{ROOT}}/StrykerOutput/ — only the latest run's HTML/JSON reports are
#     copied out at the end. The mutated sandbox subdirectories are NEVER
#     copied (they die with the container).
#
# WHAT NEVER TOUCHES THE HOST:
#   - Mutated source trees (live in /work, container overlay, --rm wipes).
#   - Stryker's per-mutation sandbox dirs inside StrykerOutput/.
[private]
_container-ephemeral +ARGS:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        # Already inside a devcontainer — that container IS the ephemeral
        # boundary. Run directly; the outer just wrapper ensures --rm.
        just {{ARGS}}
        exit 0
    fi
    mkdir -p "{{ROOT}}/StrykerOutput" \
             "{{ROOT}}/.mutants-cache/nuget" \
             "{{ROOT}}/.mutants-cache/dotnet-tools"
    docker run --rm --network=host \
        -v "{{ROOT}}:/src:ro,Z" \
        -v "{{ROOT}}/StrykerOutput:/out:Z" \
        -v "{{ROOT}}/.mutants-cache/nuget:/nuget-cache:Z" \
        -v "{{ROOT}}/.mutants-cache/dotnet-tools:/dotnet-tools:Z" \
        -v "{{ROOT}}/justfile.container:/etc/angzarr-justfile:ro" \
        -w /work \
        -e DEVCONTAINER=true \
        -e NUGET_PACKAGES=/nuget-cache \
        -e DOTNET_CLI_HOME=/dotnet-tools \
        -e DOTNET_TOOLS_PATH=/dotnet-tools \
        -e MUTANTS_EPHEMERAL=1 \
        {{IMAGE}} bash -eu -o pipefail -c '
            echo "[ephemeral] copying /src -> /work (container overlay)"
            mkdir -p /work
            # tar|tar: rsync is not guaranteed in the base image. Excludes
            # mirror what rsync would skip — build artifacts, prior mutation
            # output, and the host-side caches we mount separately.
            tar -C /src \
                --exclude=./bin \
                --exclude=./obj \
                --exclude=./.mutants-cache \
                --exclude=./StrykerOutput \
                -cf - . \
                | tar -C /work -xf -
            # Mount the container-side justfile into the copy so `just` finds
            # it (the original /src is read-only, but /work is writable).
            cp /etc/angzarr-justfile /work/justfile
            cd /work
            just {{ARGS}}
            # Persist ONLY the reports back to host. Mutated sandbox dirs
            # (StrykerOutput/<run>/sandbox-*/, etc.) die with the container.
            if [ -d /work/StrykerOutput ]; then
                echo "[ephemeral] copying Stryker reports (no sandboxes) -> /out"
                # Latest run is the most-recently-modified subdir under StrykerOutput.
                LATEST=$(ls -1dt /work/StrykerOutput/*/ 2>/dev/null | head -n1 || true)
                if [ -n "$LATEST" ]; then
                    RUN_NAME=$(basename "$LATEST")
                    mkdir -p "/out/$RUN_NAME"
                    # Copy only the reports/ subtree (HTML + JSON) — never
                    # the sandbox-*/ siblings that hold mutated source.
                    if [ -d "$LATEST/reports" ]; then
                        cp -r "$LATEST/reports" "/out/$RUN_NAME/reports"
                    fi
                    # Top-level mutation-report.json (if present at run root).
                    find "$LATEST" -maxdepth 1 -name "*.json" -exec cp {} "/out/$RUN_NAME/" \;
                    echo "[ephemeral] reports copied to host StrykerOutput/$RUN_NAME/"
                fi
            fi
        '

default:
    @just --list

# =============================================================================
# Proto generation — cross-language model (project_proto_generation_model)
# =============================================================================
# `.proto` sources live in the angzarr-project submodule. Generated C#
# bindings are NEVER committed (see .gitignore — Grpc.Tools emits to
# Angzarr.Proto/obj/<Configuration>/<TargetFramework>/Protos/ which is
# already gitignored under the global obj/ rule). They are regenerated:
#   1. on `post-checkout` / `post-merge` via lefthook (covers fresh clones,
#      branch switches, submodule bumps)
#   2. transparently as a recipe dependency of `build`, `test`, `fmt`, etc.
#      The recipe is idempotent — mtime guard skips when bindings are newer
#      than the newest .proto source.
#
# Runs in the same devcontainer image used for build/test/mutation so the
# Grpc.Tools / protoc-gen-csharp toolchain is fixed (no host fallback).
# Rootless docker requires `-u 0:0` per feedback_docker_rootless.
#
# Build-tool integration (the `Grpc.Tools` NuGet package, which ships a
# Protobuf MSBuild task) is the EXECUTOR but NOT the trigger: this recipe
# explicitly invokes `dotnet build Angzarr.Proto/Angzarr.Proto.csproj`.
# Downstream `dotnet build/test` calls of Client/Tests do transitively
# invoke Grpc.Tools again, but that pass is mtime-idempotent and a no-op
# when bindings are already fresh. Keeping the regen orchestration in
# `just` matches the 6-lang ecosystem pattern (project_proto_generation_model).

PROTO_SRC_DIR := ROOT + "/angzarr-project/proto"
PROTO_OUT_DIR := ROOT + "/Angzarr.Proto/obj"

# Public entry point. Idempotent: returns immediately if bindings are
# fresher than the newest .proto source.
generate-proto:
    #!/usr/bin/env bash
    set -euo pipefail
    src_dir="{{PROTO_SRC_DIR}}"
    out_dir="{{PROTO_OUT_DIR}}"
    if [ ! -d "$src_dir" ]; then
        echo "[generate-proto] $src_dir missing — is the angzarr-project submodule initialized?" >&2
        exit 1
    fi
    # Staleness check: regenerate if any .proto file is newer than the
    # OLDEST generated binding, or if no bindings exist yet.
    # Catches "submodule bumped" and "fresh clone" — the hot paths driving
    # the lefthook trigger. Does NOT catch manual deletion of one binding
    # while others remain fresh; use `just generate-proto-force` for that.
    #
    # OLDEST (matches Python/Java) — the Grpc.Tools output tree lives under
    # Angzarr.Proto/obj/ which msbuild wipes-and-regens on each build, so
    # no orphan-stale leftovers exist. (Go's NEWEST adaptation unnecessary.)
    newest_proto=$(find "$src_dir" -name '*.proto' -printf '%T@\n' 2>/dev/null \
                    | sort -n | tail -1)
    # Guard the find for out_dir — on clean state Angzarr.Proto/obj does
    # not yet exist, and `find $missing` exits non-zero which trips pipefail.
    # Grpc.Tools emits *.cs files mirroring the proto package path under
    # Angzarr.Proto/obj/<Configuration>/<TargetFramework>/angzarr_client/proto/angzarr/
    # (and *Grpc.cs siblings). The path component below filters out the
    # msbuild-generated AssemblyInfo/GlobalUsings .cs noise.
    if [ -d "$out_dir" ]; then
        oldest_pb=$(find "$out_dir" -path '*/angzarr_client/proto/*' -name '*.cs' \
                        -printf '%T@\n' 2>/dev/null | sort -n | head -1)
    else
        oldest_pb=""
    fi
    if [ -n "$newest_proto" ] && [ -n "$oldest_pb" ] \
        && awk -v p="$newest_proto" -v b="$oldest_pb" 'BEGIN{exit !(b>p)}'; then
        echo "[generate-proto] bindings up-to-date, skipping (use 'just generate-proto-force' to override)"
        exit 0
    fi
    just generate-proto-force

# Always regenerate, ignoring mtimes. Invoked by `generate-proto` when stale
# and exposed directly for users who want to force a rebuild.
generate-proto-force:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        # Inside the devcontainer image already — run directly.
        just --justfile "{{ROOT}}/justfile.container" generate-proto-force
        exit 0
    fi
    # Rootless docker: -u 0:0 maps to host user via subuid; writes to the
    # bind-mount land owned by the host user. Rootful: direct uid match.
    # See feedback_docker_rootless.
    if docker info --format '{{{{.SecurityOptions}}}}' 2>/dev/null | grep -q rootless; then
        USER_FLAG="-u 0:0"
    else
        USER_FLAG="-u $(id -u):$(id -g)"
    fi
    docker run --rm --network=host \
        $USER_FLAG \
        -v "{{ROOT}}:/workspace:Z" \
        -v "{{ROOT}}/justfile.container:/workspace/justfile:ro" \
        -w /workspace \
        -e DEVCONTAINER=true \
        {{IMAGE}} just generate-proto-force

# Legacy alias — kept so existing recipe-deps and muscle memory keep working.
proto: generate-proto

build: generate-proto
    just _container build

test: generate-proto
    just _container test

# Start gRPC test server for unified Rust harness testing
serve: generate-proto
    just _container serve

coverage: generate-proto
    just _container coverage

# Run Stryker.NET mutation tests inside an ephemeral container.
# Source is mounted READ-ONLY; mutated copies live in the container overlay
# and die with --rm. Only HTML/JSON reports are persisted to StrykerOutput/.
# Host dotnet/stryker invocations are FORBIDDEN — always go through `just`.
mutation-test: generate-proto
    just _container-ephemeral mutation-test

# Purge the local mutation cache (.mutants-cache/) — NuGet packages and
# dotnet-tool restore output only; never holds mutated source.
mutation-purge-cache:
    rm -rf "{{ROOT}}/.mutants-cache"
    @echo "Removed {{ROOT}}/.mutants-cache"

pack: generate-proto
    just _container pack

publish: generate-proto
    just _container publish

# Idempotent cleanup of build artifacts, generated bindings, mutation
# reports, and container caches. Cross-language convention; matches the
# `just clean` shape used by client-go (d7639e1) / client-java (d0db749).
# Safe to run multiple times.
clean:
    @echo "[clean] wiping per-project bin/ + obj/ (incl. generated proto trees)…"
    rm -rf "{{ROOT}}/Angzarr.Client/bin" "{{ROOT}}/Angzarr.Client/obj" \
           "{{ROOT}}/Angzarr.Proto/bin" "{{ROOT}}/Angzarr.Proto/obj" \
           "{{ROOT}}/Angzarr.Client.Tests/bin" "{{ROOT}}/Angzarr.Client.Tests/obj"
    find "{{ROOT}}" -type d \( -name bin -o -name obj \) \
        -not -path '*/angzarr-project/*' \
        -not -path '*/.git/*' \
        -exec rm -rf {} + 2>/dev/null || true
    @echo "[clean] wiping stray nuget output…"
    find "{{ROOT}}" -maxdepth 3 -name '*.nupkg' -delete 2>/dev/null || true
    find "{{ROOT}}" -maxdepth 3 -name '*.snupkg' -delete 2>/dev/null || true
    @echo "[clean] wiping IDE / SDK transient state…"
    rm -rf "{{ROOT}}/.vs" "{{ROOT}}/TestResults" \
           "{{ROOT}}/packages" "{{ROOT}}/.nuget-packages"
    find "{{ROOT}}" -maxdepth 3 \( -name '*.suo' -o -name '*.user' \) -delete 2>/dev/null || true
    @echo "[clean] wiping mutation reports + caches…"
    rm -rf "{{ROOT}}/StrykerOutput" "{{ROOT}}/.mutants-cache" \
           "{{ROOT}}/mutants-reports" "{{ROOT}}/reports/mutation"
    @echo "[clean] wiping container caches…"
    rm -rf "{{ROOT}}/.container-cache" "{{ROOT}}/.container-home"
    @echo "[clean] complete"

# Check formatting
fmt: generate-proto
    just _container fmt

# Auto-format code
fmt-fix: generate-proto
    just _container fmt-fix

# Cross-language alias — `just check` runs lint + fmt-check.
check: fmt

# Cross-language alias — `just lint` placeholder (C# uses fmt-check only).
lint: fmt
