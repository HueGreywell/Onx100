using Microsoft.AspNetCore.Diagnostics;
using Onx100Driver;
using Onx100Driver.Transport;

namespace Onx100Api;

public static class ExceptionHandler
{
    public static void UseDeviceExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(error => error.Run(async context =>
        {
            var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (ex is null) return;

            var (status, message) = ex switch
            {
                InvalidOperationException when ex is not OnxCommandException => (409, ex.Message),
                OnxUnavailableException => (409, ex.Message),
                OnxConnectionTakenException => (409, ex.Message),
                OnxInvalidParameterException => (400, ex.Message),
                ArgumentOutOfRangeException => (400, ex.Message),
                OnxTransportException => (502, ex.Message),
                TimeoutException => (504, ex.Message),
                OnxCommandException => (500, ex.Message),
                _ => (500, "An unexpected error occurred.")
            };

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = message });
        }));
    }
}
