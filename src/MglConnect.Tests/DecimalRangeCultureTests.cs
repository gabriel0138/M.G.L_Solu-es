using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.Extensions.Primitives;
using MglConnect.Web.ModelBinding;
using MglConnect.Web.ViewModels;
using Xunit;

namespace MglConnect.Tests;

public sealed class DecimalRangeCultureTests
{
    [Fact]
    public void DecimalRangeLimitsAreParsedInvariantlyUnderBrazilianCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");

            foreach (var type in new[] { typeof(FleetVehicleForm), typeof(TruckProfileForm) })
            foreach (var property in type.GetProperties())
            foreach (var range in property.GetCustomAttributes(typeof(RangeAttribute), inherit: true).Cast<RangeAttribute>())
            {
                if (range.OperandType != typeof(decimal))
                    continue;

                Assert.True(range.IsValid(1m), $"{type.Name}.{property.Name} must accept a valid decimal under pt-BR.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("18.60")]
    public async Task HtmlDecimalBinderPreservesDecimalValueUnderBrazilianCulture(string postedValue)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal));
            var query = new QueryCollection(new Dictionary<string, StringValues> { ["Value"] = postedValue });
            var values = new CompositeValueProvider
            {
                new QueryStringValueProvider(BindingSource.Query, query, CultureInfo.CurrentCulture)
            };
            var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            var context = DefaultModelBindingContext.CreateBindingContext(actionContext, values, metadata, null, "Value");

            await new HtmlDecimalModelBinder().BindModelAsync(context);

            Assert.True(context.Result.IsModelSet);
            Assert.Equal(18.60m, Assert.IsType<decimal>(context.Result.Model));
            Assert.Empty(context.ModelState.Values.SelectMany(state => state.Errors));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
