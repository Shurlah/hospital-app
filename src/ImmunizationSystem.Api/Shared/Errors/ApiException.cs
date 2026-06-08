using System.Net;
using System.Text.Json;

namespace ImmunizationSystem.Api.Shared.Errors;

public sealed class ApiException(string code, string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
    : Exception(message)
{
    public string Code { get; } = code;

    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException exception)
        {
            await WriteErrorAsync(context, exception.Code, exception.Message, exception.StatusCode);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API exception");
            await WriteErrorAsync(
                context,
                "INTERNAL_SERVER_ERROR",
                "An unexpected error occurred.",
                HttpStatusCode.InternalServerError);
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, string code, string message, HttpStatusCode statusCode)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new { error = new { code, message, details = Array.Empty<string>() } });
        await context.Response.WriteAsync(payload);
    }
}
