using System.Net;
using System.Text.Json;
using Xunit;
namespace MglConnect.Tests;
internal static class RoadApprovalHttp
{
    public static Dictionary<string,string> Configuration(){
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"MglConnect.sln")))root=root.Parent;
        return new(){{"ASPNETCORE_ENVIRONMENT","Development"},{"DemoData__Enabled","true"},{"Routing__HistoricalGeometryPath",Path.Combine(root!.FullName,"src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json")}};
    }
    public static async Task Publish(SecurityHttpFixture.Session admin,int id){
        var html=await admin.Client.GetStringAsync("/Roteamento?entregaId="+id);
        var binding=System.Text.RegularExpressions.Regex.Match(html,"name=\"binding\" value=\"([A-F0-9]+)\"").Groups[1].Value;
        var fields=new Dictionary<string,string>{{"entregaId",id.ToString()},{"binding",binding},{"confirmedStops",JsonSerializer.Serialize(new[]{new MglConnect.Web.Services.ConfirmedRouteStop("Public origin",-23.5614,-46.6559,"Map",true),new MglConnect.Web.Services.ConfirmedRouteStop("Public destination",-23.5505,-46.6333,"Map",true)})},{"__RequestVerificationToken",await SecurityHttpFixture.AntiForgeryAsync(admin.Client,"/Roteamento?entregaId="+id)}};
        using var preview=await admin.Client.PostAsync("/Roteamento/Previa",new FormUrlEncodedContent(fields));Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
        var reply=JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement;Assert.True(reply.GetProperty("available").GetBoolean());fields["approvalId"]=reply.GetProperty("approvalId").GetString()!;
        using var published=await admin.Client.PostAsync("/Roteamento/Confirmar",new FormUrlEncodedContent(fields));Assert.Equal(HttpStatusCode.Redirect,published.StatusCode);
    }
}
