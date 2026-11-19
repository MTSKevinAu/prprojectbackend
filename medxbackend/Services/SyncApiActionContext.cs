using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace medxbackend.Services;

public sealed class SyncApiActionContext : IActionFilter
{
    public ActionContext? Current { get; private set; }

    public void OnActionExecuting(ActionExecutingContext context) => Current = context;

    public void OnActionExecuted(ActionExecutedContext context) => Current = null;
}
