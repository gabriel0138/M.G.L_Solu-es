using System.ComponentModel.DataAnnotations;

namespace MglConnect.Web.ViewModels;

public class NovoPedidoClienteViewModel
{
    [Required, StringLength(36)]
    public string ChaveEnvio { get; set; } = Guid.NewGuid().ToString("N");

    [Required, StringLength(255)]
    [Display(Name = "Descrição / itens")]
    public string Descricao { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Valor total (R$)")]
    public decimal ValorTotal { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Peso da carga (kg)")]
    public decimal PesoKg { get; set; }

    [Required, StringLength(50)]
    [Display(Name = "Tipo de carga")]
    public string TipoCarga { get; set; } = "Carga Geral Seca";

    [Required, StringLength(200)]
    [Display(Name = "Logradouro de coleta")]
    public string OrigemEndereco { get; set; } = string.Empty;

    [Required, StringLength(20)]
    [Display(Name = "Número de coleta")]
    public string OrigemNumero { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "Bairro de coleta")]
    public string? OrigemBairro { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Cidade de coleta")]
    public string OrigemCidade { get; set; } = string.Empty;

    [Required, StringLength(2)]
    [RegularExpression("^[A-Za-z]{2}$")]
    [Display(Name = "UF de coleta")]
    public string OrigemEstado { get; set; } = string.Empty;

    [StringLength(10)]
    [Display(Name = "CEP de coleta")]
    public string? OrigemCep { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Logradouro de destino")]
    public string DestinoEndereco { get; set; } = string.Empty;

    [Required, StringLength(20)]
    [Display(Name = "Número de destino")]
    public string DestinoNumero { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "Bairro de destino")]
    public string? DestinoBairro { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Cidade de destino")]
    public string DestinoCidade { get; set; } = string.Empty;

    [Required, StringLength(2)]
    [RegularExpression("^[A-Za-z]{2}$")]
    [Display(Name = "UF de destino")]
    public string DestinoEstado { get; set; } = string.Empty;

    [StringLength(10)]
    [Display(Name = "CEP de destino")]
    public string? DestinoCep { get; set; }

    [Range(typeof(decimal), "0.01", "20", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Altura da carga (m)")]
    public decimal? AlturaCargaMetros { get; set; }

    [Range(typeof(decimal), "0.01", "20", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Largura da carga (m)")]
    public decimal? LarguraCargaMetros { get; set; }

    [Range(typeof(decimal), "0.01", "30", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Comprimento da carga (m)")]
    public decimal? ComprimentoCargaMetros { get; set; }
}
