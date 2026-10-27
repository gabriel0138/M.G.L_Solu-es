using System;

namespace MglConnect.Web.Services
{
    public interface IAnttFreteService
    {
        /// <summary>
        /// Calcula o frete mínimo ANTT para a rota com base em distância, eixos e tipo de carga.
        /// </summary>
        AnttFreteResult CalcularFreteMinimoAntt(decimal distanciaKm, int qtdEixos, string tipoCarga);

        /// <summary>
        /// Compara o valor cotado com o frete mínimo ANTT e retorna indicador de viabilidade.
        /// </summary>
        AnttViabilidadeResult VerificarViabilidadeFinanceira(decimal valorCotado, decimal distanciaKm, int qtdEixos, string tipoCarga);

        /// <summary>
        /// Calcula pedágio com suporte a eixos suspensos (retorno de veículo vazio = eixos/2 + 1).
        /// </summary>
        decimal CalcularPedagioComEixosSuspensos(int qtdEixos, bool retornoVazio = false);
    }

    public record AnttFreteResult(
        decimal ValorMinimoReais,
        decimal TaxaBasePorKm,
        int QtdEixos,
        string TipoCarga,
        decimal DistanciaKm,
        string Tabela
    );

    public record AnttViabilidadeResult(
        bool Viavel,
        decimal ValorCotado,
        decimal ValorMinimoAntt,
        decimal DiferencaReais,
        decimal MargemPercentual,
        string Status,
        string CorIndicador,
        string Mensagem
    );
}
