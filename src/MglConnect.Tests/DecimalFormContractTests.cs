using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using MglConnect.Web.ModelBinding;
using MglConnect.Web.Services.RouteTrust;
using MglConnect.Web.ViewModels;
using Xunit;

namespace MglConnect.Tests;

public sealed class DecimalFormContractTests
{
    public static IEnumerable<object[]> ValidNumbers()
    {
        foreach (var culture in new[] { "pt-BR", "en-US", "" })
        foreach (var (text, number) in new[] { ("18.60", 18.60m), ("0", 0m), ("0.01", .01m),
            ("-18.60", -18.60m), ("-0.00", 0m), ("00018.60", 18.60m), ("99999999.99", 99999999.99m),
            ("79228162514264337593543950335", decimal.MaxValue),
            ("7922816251426433759354395033.5", 7922816251426433759354395033.5m) })
            yield return [culture, text, number];
    }

    public static IEnumerable<object[]> InvalidNumbers()
    {
        foreach (var culture in new[] { "pt-BR", "en-US", "" })
        foreach (var text in new[] { "18,60", "1,234", "1.234", "1,234.56", "1.234,56", "1,000",
            "1,23,4.56", "1,,2.34", "1.2.3", ".50", "1.", "-", "+1", "1-", "1+", "1e2", "1E-2",
            "R$1.00", "$1.00", "(1.00)", "NaN", "Infinity", " 1.00", "1.00 ", " ", "\t1", "1\n",
            "１.００", "١.٢", "1.000", "0.001", "1.230", "79228162514264337593543950336",
            "7922816251426433759354395033.51", "99999999999999999999999999999999999999" })
            yield return [culture, text];
    }

    [Theory, MemberData(nameof(ValidNumbers))]
    public async Task BinderAcceptsExactInvariantValuesRegardlessOfCulture(string culture, string text, decimal expected)
    {
        using var scope = new CultureScope(culture);
        foreach (var type in new[] { typeof(decimal), typeof(decimal?) })
        {
            var context = await Bind(type, text);
            Assert.True(context.Result.IsModelSet);
            Assert.Equal(expected, Assert.IsType<decimal>(context.Result.Model));
            Assert.Empty(context.ModelState["Value"]!.Errors);
            Assert.Equal(text, context.ModelState["Value"]!.AttemptedValue);
        }
    }

    [Theory, MemberData(nameof(InvalidNumbers))]
    public async Task BinderRejectsAmbiguityAndRoundingWithoutLosingAttemptedValue(string culture, string text)
    {
        using var scope = new CultureScope(culture);
        foreach (var type in new[] { typeof(decimal), typeof(decimal?) })
        {
            var context = await Bind(type, text);
            Assert.False(context.Result.IsModelSet);
            Assert.Single(context.ModelState["Value"]!.Errors);
            Assert.Equal(text, context.ModelState["Value"]!.AttemptedValue);
        }
    }

    [Theory, InlineData("pt-BR"), InlineData("en-US"), InlineData("")]
    public async Task MissingEmptyAndRepeatedValuesHaveExplicitSemantics(string culture)
    {
        using var scope = new CultureScope(culture);
        var empty = await Bind(typeof(decimal?), "");
        Assert.True(empty.Result.IsModelSet); Assert.Null(empty.Result.Model);
        Assert.Empty(empty.ModelState["Value"]!.Errors);
        Assert.Single((await Bind(typeof(decimal), "")).ModelState["Value"]!.Errors);
        var missing = await Bind(typeof(decimal?), null);
        Assert.False(missing.Result.IsModelSet); Assert.Empty(missing.ModelState);
        var repeated = await Bind(typeof(decimal), "1.00", "2.00");
        Assert.False(repeated.Result.IsModelSet); Assert.Single(repeated.ModelState["Value"]!.Errors);
    }

    [Theory, InlineData("pt-BR"), InlineData("en-US"), InlineData("")]
    public void EveryMeasureKeepsItsRangeAndCentPrecision(string culture)
    {
        using var scope = new CultureScope(culture);
        foreach (var type in new[] { typeof(FleetVehicleForm), typeof(TruckProfileForm) })
        foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?)))
        {
            var range = Assert.Single(property.GetCustomAttributes(typeof(RangeAttribute), true).Cast<RangeAttribute>());
            var min = decimal.Parse((string)range.Minimum, CultureInfo.InvariantCulture);
            var max = decimal.Parse((string)range.Maximum, CultureInfo.InvariantCulture);
            Assert.True(range.IsValid(min)); Assert.True(range.IsValid(max));
            Assert.False(range.IsValid(min - .01m)); Assert.False(range.IsValid(max + .01m));
            Assert.False(range.IsValid(-1m));
            var model = Activator.CreateInstance(type)!;
            property.SetValue(model, 1.001m);
            var errors = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), errors, true);
            // IValidatableObject may be skipped after unrelated property errors;
            // invoke the same object-level contract explicitly as well.
            errors.AddRange(((IValidatableObject)model).Validate(new ValidationContext(model)));
            Assert.Contains(errors, e => e.MemberNames.Contains(property.Name));
        }
    }

    [Theory, InlineData("pt-BR"), InlineData("en-US"), InlineData("")]
    public void AxleListUsesTheSameGrammarAndDomainValidatesItsValues(string culture)
    {
        using var scope = new CultureScope(culture);
        var form = new TruckProfileForm { AxleLoadsKg = "15000.01;15000.01", Axles = 2, Pbt = 30000.02m };
        var profile = form.ToProfile(1);
        Assert.Equal(new[] { 15000.01m, 15000.01m }, profile.AxleLoads.Select(m => m.Value));
        Assert.Empty(profile.Validate());
        Assert.Equal("15000.01;15000.01", TruckProfileForm.From(profile, 0).AxleLoadsKg);
        Assert.Empty(new TruckProfileForm().ToProfile(1).AxleLoads);
        foreach (var invalid in new[] { "18,60", "1,234.56", "1.234", ";1", "1;", "1;;2", "1; 2", " ", "1e2", "7922816251426433759354395033.51" })
        {
            var bad = new TruckProfileForm { AxleLoadsKg = invalid };
            Assert.Contains(bad.Validate(new ValidationContext(bad)), e => e.MemberNames.Contains(nameof(bad.AxleLoadsKg)));
            Assert.Throws<FormatException>(() => bad.ToProfile(1));
        }
        foreach (var value in new[] { -1m, 0m, .001m })
        {
            var bad = new OperationalTruckProfile { Axles = 1, AxleLoads = [new(value, "kg", FactOrigin.Declarado, "fixture")] };
            Assert.NotEmpty(bad.Validate());
        }
    }

    private static async Task<ModelBindingContext> Bind(Type type, string? text, string? second = null)
    {
        var query = new QueryCollection(text == null ? new Dictionary<string, StringValues>() :
            new Dictionary<string, StringValues> { ["Value"] = second == null ? new StringValues(text) : new StringValues([text, second]) });
        var context = DefaultModelBindingContext.CreateBindingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new QueryStringValueProvider(BindingSource.Query, query, CultureInfo.CurrentCulture),
            new EmptyModelMetadataProvider().GetMetadataForType(type), null, "Value");
        await new HtmlDecimalModelBinder().BindModelAsync(context);
        return context;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
        public CultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() { CultureInfo.CurrentCulture = _culture; CultureInfo.CurrentUICulture = _ui; }
    }
}
