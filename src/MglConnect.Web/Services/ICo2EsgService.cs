using System;
using System.Collections.Generic;

namespace MglConnect.Web.Services
{
    public interface ICo2EsgService
    {
        Co2Result CalcularEmissoesCo2(decimal distanciaKm, string tipoCombustivel, decimal consumoKmL);
        EsgFleetReport GerarRelatorioFrota(IEnumerable<Co2Result> resultados);
    }

    public record Co2Result(
        decimal DistanciaKm,
        string TipoCombustivel,
        decimal ConsumoKmL,
        decimal LitrosConsumidos,
        decimal EmissaoCo2Kg,
        decimal EmissaoCo2Toneladas,
        string ClassificacaoEmissao,
        string CorIndicador,
        decimal EquivalenteArvores
    );

    public record EsgFleetReport(
        decimal TotalCo2Kg,
        decimal TotalCo2Toneladas,
        decimal TotalKmPercorridos,
        decimal TotalLitrosConsumidos,
        decimal MediaEmissaoPorKm,
        decimal EquivalenteArvoresParaNeutralizar,
        string ClassificacaoGeral,
        string Recomendacao
    );
}
