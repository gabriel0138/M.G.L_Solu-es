using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace MglConnect.Web.Services;

// Ephemeral, bounded, server-owned approvals. A restart/eviction fails closed.
public sealed record RoadPreviewSnapshot(Guid TenantId,int ActorId,int TripId,int BaseRevision,string Binding,
    DateTime ExpiresAtUtc,string StopsJson,string PlanJson);
public sealed class RoadPreviewStore : IDisposable
{
    private readonly MemoryCache cache=new(new MemoryCacheOptions{SizeLimit=32*1024*1024});
    private readonly object gate=new();
    public string? Put(RoadPreviewSnapshot snapshot)
    {
        var size=Encoding.UTF8.GetByteCount(snapshot.StopsJson)+Encoding.UTF8.GetByteCount(snapshot.PlanJson)+512;
        if(size>2*1024*1024)return null;
        var id=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock(gate)cache.Set(id,snapshot,new MemoryCacheEntryOptions{Size=size,AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(5)});
        return id;
    }
    public RoadPreviewSnapshot? Take(string? id,int actor,int trip,Guid tenant)
    {
        if(id is not {Length:64})return null;
        lock(gate){
            if(!cache.TryGetValue<RoadPreviewSnapshot>(id,out var s)||s==null||s.ActorId!=actor||s.TripId!=trip||s.TenantId!=tenant)return null;
            cache.Remove(id);return s;
        }
    }
    public void Dispose()=>cache.Dispose();
}
public sealed record ApprovedRoadPreview(TruckRoutePlan Plan,string? ApprovalId,DateTime ExpiresAtUtc,int BaseRevision);
public sealed record RoadPublicationResult(string Status,TruckRoutePlan? Published=null,ApprovedRoadPreview? Replacement=null,TruckRoutePlan? Previous=null);
