using System;

namespace MglConnect.Web.Services
{
    /// <summary>
    /// Motor de Pedágios e Frete Mínimo ANTT — Resolução ANTT nº 6.231/2021 e tabela 2024.
    /// Inclui suporte a eixos suspensos (veículo vazio) e tipos de carga.
    /// </summary>
    public class AnttFreteService : IAnttFreteService
    {
        // ══════════════════════════════════════════════════════════════
        // TABELA ANTT — Frete Mínimo por Eixo (R$/km) — Referência 2024
        // Fonte: ANTT Resolução 6.231/2021 atualizada INPC 2024
        // ══════════════════════════════════════════════════════════════
        // Eixos comerciais contados (não inclui o da direção):
        // 2 eixos: caminhão toco (≤ 5t PBT)
        // 3 eixos: caminhão truck
        // 4 eixos: bi-trem / carreta simples
        // 5 eixos: carreta rodotrem
        // 6 eixos: bitrem graneleiro / rodotrem 6 eixos
        // 7 eixos: tritrem / rodotrem 7 eixos
        // 9 eixos: rodotrem 9 eixos

        private static readonly (int eixos, decimal taxaKm, string descricao)[] TabelaAntt = new[]
        {
            (2, 3.5198m,  "Caminhão Toco 2 eixos"),
            (3, 4.8423m,  "Caminhão Truck 3 eixos"),
            (4, 5.9547m,  "Carreta Simples 4 eixos"),
            (5, 6.7712m,  "Carreta / Rodotrem 5 eixos"),
            (6, 7.8614m,  "Bi-trem / Rodotrem 6 eixos"),
            (7, 9.0821m,  "Tritrem 7 eixos"),
            (9, 11.2483m, "Rodotrem 9 eixos")
        };

        // Multiplicadores de tipo de carga sobre o frete mínimo base
        private static readonly (string tipo, decimal fator)[] FatoresCarga = new[]
        {
            ("Granel Sólido",        0.87m),
            ("Carga Geral Seca",     1.00m),
            ("Frigorífico",          1.18m),
            ("Carga Indivisível",    1.35m),
            ("Produtos Perigosos",   1.42m),
            ("Granéis Líquidos",     0.92m),
            ("Carga Pesada Indivisível", 1.50m)
        };

        // Taxa base de pedágio por praça (tarifas ARTESP SP 2024 — R$ por eixo)
        private const decimal TaxaPedagioBasePortico    = 7.20m;  // portão único
        private const decimal TotalPraçasRotaCarjamar  = 6;       // praças na rota Cajamar→Santos

        public AnttFreteResult CalcularFreteMinimoAntt(decimal distanciaKm, int qtdEixos, string tipoCarga)
        {
            if (distanciaKm <= 0) distanciaKm = 1m;
            if (qtdEixos < 2) qtdEixos = 2;

            var taxaBase = ObterTaxaBasePorEixo(qtdEixos);
            var fatorCarga = ObterFatorCarga(tipoCarga);
            var taxaEfetiva = taxaBase * fatorCarga;
            var valorMinimo = Math.Round(taxaEfetiva * distanciaKm, 2);

            return new AnttFreteResult(
                ValorMinimoReais: valorMinimo,
                TaxaBasePorKm: taxaBase,
                QtdEixos: qtdEixos,
                TipoCarga: tipoCarga,
                DistanciaKm: distanciaKm,
                Tabela: $"ANTT Res. 6.231/2021 — {qtdEixos} eixos × R$ {taxaBase:F4}/km × Fator carga {fatorCarga:F2}"
            );
        }

        public AnttViabilidadeResult VerificarViabilidadeFinanceira(decimal valorCotado, decimal distanciaKm, int qtdEixos, string tipoCarga)
        {
            var antt = CalcularFreteMinimoAntt(distanciaKm, qtdEixos, tipoCarga);
            var diferenca = valorCotado - antt.ValorMinimoReais;
            var margem = antt.ValorMinimoReais > 0
                ? Math.Round((diferenca / antt.ValorMinimoReais) * 100m, 2)
                : 0m;

            bool viavel = diferenca >= 0;
            string status, cor, mensagem;

            if (margem >= 20m)
            {
                status = "ÓTIMO";
                cor = "#10B981";
                mensagem = $"Margem saudável de {margem:F1}% acima do piso ANTT. Viagem economicamente viável.";
            }
            else if (margem >= 5m)
            {
                status = "ACEITÁVEL";
                cor = "#F59E0B";
                mensagem = $"Margem reduzida de {margem:F1}%. Verifique o custo de combustível e pedágios antes de expedir.";
            }
            else if (margem >= 0m)
            {
                status = "CRÍTICO";
                cor = "#EF4444";
                mensagem = $"Margem mínima ({margem:F1}%). Frete coberto, mas risco operacional elevado.";
            }
            else
            {
                status = "INVIÁVEL";
                cor = "#7F1D1D";
                mensagem = $"Valor cotado R$ {valorCotado:N2} está R$ {Math.Abs(diferenca):N2} ABAIXO do frete mínimo ANTT (R$ {antt.ValorMinimoReais:N2}). RISCO LEGAL!";
            }

            return new AnttViabilidadeResult(
                Viavel: viavel,
                ValorCotado: valorCotado,
                ValorMinimoAntt: antt.ValorMinimoReais,
                DiferencaReais: diferenca,
                MargemPercentual: margem,
                Status: status,
                CorIndicador: cor,
                Mensagem: mensagem
            );
        }

        public decimal CalcularPedagioComEixosSuspensos(int qtdEixos, bool retornoVazio = false)
        {
            // Em retorno vazio: eixos suspensos (pneumáticos levantados) = redução na categoria de cobrança
            int eixosEfetivos = retornoVazio
                ? Math.Max(2, (qtdEixos / 2) + 1)
                : qtdEixos;

            return Math.Round(eixosEfetivos * TaxaPedagioBasePortico * TotalPraçasRotaCarjamar, 2);
        }

        // ── HELPERS PRIVADOS ──────────────────────────────────────────

        private static decimal ObterTaxaBasePorEixo(int qtdEixos)
        {
            // Encontra o intervalo mais próximo por baixo
            decimal taxa = TabelaAntt[0].taxaKm;
            foreach (var (eixos, taxaKm, _) in TabelaAntt)
            {
                if (qtdEixos >= eixos)
                    taxa = taxaKm;
            }
            return taxa;
        }

        private static decimal ObterFatorCarga(string tipoCarga)
        {
            if (string.IsNullOrWhiteSpace(tipoCarga)) return 1.00m;
            foreach (var (tipo, fator) in FatoresCarga)
            {
                if (tipoCarga.Contains(tipo, StringComparison.OrdinalIgnoreCase))
                    return fator;
            }
            return 1.00m; // default: carga geral seca
        }
    }
}
