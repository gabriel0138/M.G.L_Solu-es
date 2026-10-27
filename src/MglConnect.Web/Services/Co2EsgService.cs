using System;
using System.Collections.Generic;
using System.Linq;

namespace MglConnect.Web.Services
{
    /// <summary>
    /// Calculadora ESG de Pegada de Carbono — Pilar Estratégico 5 M.G.L Connect SaaS B2B.
    /// Fatores de emissão IPCC AR6 / CETESB 2024 (g CO₂e / litro de combustível).
    /// </summary>
    public class Co2EsgService : ICo2EsgService
    {
        // ══════════════════════════════════════════════════════════════
        // FATORES DE EMISSÃO — g CO₂ / litro (IPCC / CETESB 2024)
        // ══════════════════════════════════════════════════════════════
        private static readonly Dictionary<string, decimal> FatoresEmissao = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Diesel S10",    2680m },  // g CO₂e / litro (ciclo completo)
            { "Diesel S500",   2720m },
            { "Gasolina",      2312m },
            { "Gasolina Comum / Flex", 2312m },
            { "Etanol",        1590m },  // Ciclo curto (biogênico)
            { "GNV",           1980m },
            { "Arla 32",          0m },  // Arla não emite CO₂ direto — reduz NOx
            { "Elétrico",         0m },
        };

        // 1 árvore sequestra em média 12 kg CO₂/ano (INPE 2023)
        private const decimal Co2PorArvoreAnual = 12m;

        public Co2Result CalcularEmissoesCo2(decimal distanciaKm, string tipoCombustivel, decimal consumoKmL)
        {
            if (distanciaKm <= 0) distanciaKm = 1m;
            if (consumoKmL <= 0) consumoKmL = 2.8m; // média caminhão pesado

            decimal litros = Math.Round(distanciaKm / consumoKmL, 2);
            decimal fatorGrama = ObterFatorEmissao(tipoCombustivel);
            decimal co2Kg = Math.Round(litros * fatorGrama / 1000m, 3);
            decimal co2Ton = Math.Round(co2Kg / 1000m, 5);
            decimal arvores = Math.Round(co2Kg / Co2PorArvoreAnual, 1);

            var (classi, cor) = ClassificarEmissao(co2Kg, distanciaKm);

            return new Co2Result(
                DistanciaKm: distanciaKm,
                TipoCombustivel: tipoCombustivel,
                ConsumoKmL: consumoKmL,
                LitrosConsumidos: litros,
                EmissaoCo2Kg: co2Kg,
                EmissaoCo2Toneladas: co2Ton,
                ClassificacaoEmissao: classi,
                CorIndicador: cor,
                EquivalenteArvores: arvores
            );
        }

        public EsgFleetReport GerarRelatorioFrota(IEnumerable<Co2Result> resultados)
        {
            var lista = resultados.ToList();
            if (!lista.Any())
            {
                return new EsgFleetReport(0, 0, 0, 0, 0, 0, "N/D", "Sem dados de rotas concluídas.");
            }

            decimal totalCo2Kg = lista.Sum(r => r.EmissaoCo2Kg);
            decimal totalKm = lista.Sum(r => r.DistanciaKm);
            decimal totalLitros = lista.Sum(r => r.LitrosConsumidos);
            decimal mediaKm = totalKm > 0 ? Math.Round(totalCo2Kg / totalKm, 4) : 0m;
            decimal arvores = Math.Round(totalCo2Kg / Co2PorArvoreAnual, 0);

            var (classi, _) = ClassificarEmissao(totalCo2Kg / Math.Max(1, lista.Count), totalKm / Math.Max(1, lista.Count));
            string recomendacao = classi switch
            {
                "A" => "Excelente! Frota com emissões reduzidas. Considere relatar no GHG Protocol.",
                "B" => "Bom desempenho. Avalie uso de Arla 32 e revisão de pneus para melhorar.",
                "C" => "Emissões moderadas. Avalie renovação de frota com veículos Euro 6.",
                "D" => "Emissões elevadas. Implante telemetria de condução econômica e revise rotas.",
                _   => "Emissões críticas. Ação imediata recomendada: auditoria de frota e rotas."
            };

            return new EsgFleetReport(
                TotalCo2Kg: Math.Round(totalCo2Kg, 2),
                TotalCo2Toneladas: Math.Round(totalCo2Kg / 1000m, 4),
                TotalKmPercorridos: totalKm,
                TotalLitrosConsumidos: totalLitros,
                MediaEmissaoPorKm: mediaKm,
                EquivalenteArvoresParaNeutralizar: arvores,
                ClassificacaoGeral: classi,
                Recomendacao: recomendacao
            );
        }

        // ── HELPERS PRIVADOS ──────────────────────────────────────────

        private static decimal ObterFatorEmissao(string tipoCombustivel)
        {
            if (string.IsNullOrWhiteSpace(tipoCombustivel)) return FatoresEmissao["Diesel S10"];
            foreach (var kv in FatoresEmissao)
            {
                if (tipoCombustivel.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return FatoresEmissao["Diesel S10"];
        }

        /// <summary>
        /// Classifica emissão média por km: A (excelente) a E (crítico).
        /// Referência: CETESB 2024 — caminhões pesados Diesel.
        /// </summary>
        private static (string classe, string cor) ClassificarEmissao(decimal co2Kg, decimal distanciaKm)
        {
            if (distanciaKm <= 0) return ("N/D", "#64748B");
            decimal gPorKm = co2Kg * 1000m / distanciaKm;

            return gPorKm switch
            {
                <= 600m  => ("A", "#10B981"), // verde — excelente
                <= 800m  => ("B", "#22C55E"), // verde-claro — bom
                <= 1000m => ("C", "#F59E0B"), // âmbar — moderado
                <= 1200m => ("D", "#EF4444"), // vermelho — elevado
                _        => ("E", "#7F1D1D")  // vermelho escuro — crítico
            };
        }
    }
}
