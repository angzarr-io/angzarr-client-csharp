using System;
using System.Collections.Generic;

namespace Angzarr.Client;

/// <summary>
/// Structured build-time error raised by the unified
/// <see cref="UnifiedRouter"/> when a registration or validation invariant
/// is violated. Spec HIGH-4.3 — audit #72 cross-language shape.
///
/// <para>Parallels Java's <c>BuildException</c>, Rust's <c>BuildError</c>,
/// and Python's <c>BuildError</c>. Carries a stable
/// <see cref="ClientError.Code"/> drawn from <see cref="ErrorCodes"/>
/// and structured <see cref="ClientError.Details"/> keyed by
/// <see cref="ErrorKeys"/>.</para>
/// </summary>
public class BuildException : ClientError
{
    public BuildException(string message, string code, IDictionary<string, string>? details = null)
        : base(message, code: code, details: details)
    {
    }

    public BuildException(
        string message,
        string code,
        Exception inner,
        IDictionary<string, string>? details = null)
        : base(message, inner, code: code, details: details)
    {
    }
}
