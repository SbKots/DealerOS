using System.Text.Json;
using DealerOS.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.Api.Infrastructure;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request was cancelled by the client. CorrelationId={CorrelationId}", context.TraceIdentifier);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted) throw;
            var (status, title, code) = exception switch
            {
                DomainException domain => (400, domain.Message, domain.Code),
                ConflictException conflict => (409, conflict.Message, conflict.Code),
                ForbiddenException forbidden => (403, forbidden.Message, "forbidden"),
                NotFoundException notFound => (404, notFound.Message, "not_found"),
                BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                    (413, "Размер запроса превышает допустимый предел.", "payload_too_large"),
                BadHttpRequestException => (400, "Тело запроса имеет неверный формат.", "invalid_request"),
                JsonException => (400, "Тело запроса имеет неверный формат.", "invalid_json"),
                DbUpdateConcurrencyException => (409, "Данные были изменены другим пользователем. Обновите страницу.", "concurrency_conflict"),
                StorageUnavailableException => (503, "Хранилище фотографий временно недоступно.", "storage_unavailable"),
                _ => (500, "Произошла внутренняя ошибка.", "internal_error")
            };

            if (status == 500) logger.LogError(exception, "Unhandled request error. CorrelationId={CorrelationId}", context.TraceIdentifier);
            else logger.LogInformation("Request rejected: {Code}. CorrelationId={CorrelationId}", code, context.TraceIdentifier);

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code, ["correlationId"] = context.TraceIdentifier }
            };
            await JsonSerializer.SerializeAsync(context.Response.Body, problem, cancellationToken: context.RequestAborted);
        }
    }
}
