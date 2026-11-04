using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers;

[Authorize(Roles="Admin,Motorista")]
[Route("Entregas/{id:int}/Demonstracao")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ControlledDemoController(ControlledDemoNavigation navigation, ControlledRouteDemo demo) : Controller
{
    [HttpGet]
    public async Task<IActionResult> State(int id,CancellationToken ct)
    {
        if(await navigation.ResolveAsync(User,id,ct)==null)return NotFound();
        return Json(new { plan=await demo.ForTripAsync(id,ct),latest=await demo.LatestAsync(id,ct) });
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Capture(int id,[FromBody] DemoSampleInput? input,CancellationToken ct)
    {
        if(input==null||!ModelState.IsValid)return BadRequest();
        var result=await navigation.CaptureAsync(User,id,input,ct);
        return result==null?NotFound():Json(result);
    }
}
