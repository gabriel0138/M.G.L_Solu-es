using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MglConnect.Web.ModelBinding;

public sealed class HtmlDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        if (valueResult.Values.Count != 1)
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, DecimalFormContract.Error);
            return Task.CompletedTask;
        }
        var rawValue = valueResult.FirstValue;
        if (string.IsNullOrEmpty(rawValue))
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelMetadata.ModelType) != null)
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Informe um número válido.");

            return Task.CompletedTask;
        }

        if (DecimalFormContract.TryParse(rawValue, out var value))
            bindingContext.Result = ModelBindingResult.Success(value);
        else
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, DecimalFormContract.Error);

        return Task.CompletedTask;
    }
}
