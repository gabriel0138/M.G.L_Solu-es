using System.Text.Json;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services.RouteTrust;

public static class TripProfiles
{
    public static OperationalTruckProfile Read(Entrega trip)
    {
        if (trip.PerfilViagemJson != null)
        {
            var profile = OperationalTruckProfile.Read(trip.PerfilViagemJson);
            return profile.TripId == trip.Id && profile.VehicleId == trip.VeiculoId ? profile : new();
        }
        // Read-only compatibility: do not copy, mutate or reuse another trip's declaration.
        var legacy = OperationalTruckProfile.Read(trip.Veiculo?.PerfilOperacionalJson);
        if (legacy.TripId != trip.Id || legacy.VehicleId.HasValue && legacy.VehicleId != trip.VeiculoId) return new();
        legacy.VehicleId = trip.VeiculoId;
        return legacy;
    }
    public static int Revision(Entrega trip) => trip.PerfilViagemJson != null
        ? trip.PerfilViagemVersao : Read(trip).TripId == trip.Id ? trip.Veiculo?.PerfilOperacionalVersao ?? 0 : 0;

    public static string VehicleSnapshot(Veiculo vehicle) => JsonSerializer.Serialize(new
    {
        vehicle.Id, vehicle.Placa, vehicle.AlturaMetros, vehicle.LarguraMetros, vehicle.ComprimentoMetros,
        vehicle.PesoToneladas, vehicle.QtdEixos, vehicle.CapacidadeCargaKg, vehicle.TipoCarga,
        Origin = "Cadastro declarado/legado; não é medição nem PBT/PBTC comprovado. Não preenche o envelope da viagem."
    });
}
