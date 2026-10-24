namespace Pinqponq.OpenApi;

/// <summary>Which operations get a security requirement in the document.</summary>
public enum SecurityRequirementScope
{
    /// <summary>
    /// Operations whose endpoint has authorization metadata (<c>[Authorize]</c> or <c>RequireAuthorization()</c>)
    /// and no <c>[AllowAnonymous]</c>. An operation without endpoint metadata also gets no requirement. A fallback authorization policy is not read, so an operation that is
    /// protected only by the fallback policy gets no requirement.
    /// </summary>
    AuthorizedOperations = 0,

    /// <summary>Every operation, including anonymous ones.</summary>
    AllOperations = 1,
}
