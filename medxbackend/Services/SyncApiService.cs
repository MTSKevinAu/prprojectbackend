using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Npgsql;

namespace medxbackend.Services;

public abstract class SyncApiService(SyncApiActionContext actionContext)
{
    protected static bool IsCentreIdConflict(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.RaiseException
        && (exception.MessageText.StartsWith(
                "Cannot generate centreid: dailyno gaps exist for sequence_date ", StringComparison.Ordinal)
            || exception.MessageText.StartsWith(
                "Cannot generate centreid: duplicate records exist for sequence_date ", StringComparison.Ordinal));

    private ActionContext ActionContext => actionContext.Current
        ?? throw new InvalidOperationException("Synchronization APIs require an active MVC action context.");

    protected HttpResponse Response => ActionContext.HttpContext.Response;
    protected ModelStateDictionary ModelState => ActionContext.ModelState;

    protected bool TryValidateModel(object model)
    {
        var validator = ActionContext.HttpContext.RequestServices.GetRequiredService<IObjectModelValidator>();
        validator.Validate(ActionContext, null, string.Empty, model);
        return ModelState.IsValid;
    }

    protected ObjectResult Problem(int statusCode, string title, string? detail = null)
    {
        var factory = ActionContext.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(
            ActionContext.HttpContext, statusCode, title, detail: detail);
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    protected ActionResult ValidationProblem(ModelStateDictionary modelState)
    {
        var factory = ActionContext.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(ActionContext.HttpContext, modelState);
        return new BadRequestObjectResult(problem);
    }

    protected static OkObjectResult Ok(object? value) => new(value);
    protected static ObjectResult StatusCode(int statusCode, object? value) =>
        new(value) { StatusCode = statusCode };
    protected static NoContentResult NoContent() => new();
}
