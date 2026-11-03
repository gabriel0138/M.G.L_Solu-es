using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("Pedidos")]
    public class Pedido : ITenantOwned
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── Multi-Tenancy: Isolamento por empresa SaaS ──
        public Guid TenantId { get; set; } = Guid.Empty;

        [Required]
        [StringLength(30)]
        [Display(Name = "Número do Pedido")]
        public string NumeroPedido { get; set; } = string.Empty;

        [StringLength(36)]
        public string? ChaveIdempotencia { get; set; }

        public int Versao { get; set; }

        [Required(ErrorMessage = "Selecione o cliente.")]
        [Display(Name = "Cliente")]
        public int ClienteId { get; set; }

        [ForeignKey("ClienteId")]
        public Cliente? Cliente { get; set; }

        [Required(ErrorMessage = "A descrição do pedido é obrigatória.")]
        [StringLength(255)]
        [Display(Name = "Descrição / Itens")]
        public string Descricao { get; set; } = string.Empty;

        [StringLength(200)]
        [Display(Name = "Endereço de coleta")]
        public string? OrigemEndereco { get; set; }

        [StringLength(20)]
        public string? OrigemNumero { get; set; }

        [StringLength(80)]
        public string? OrigemBairro { get; set; }

        [StringLength(80)]
        public string? OrigemCidade { get; set; }

        [StringLength(2)]
        public string? OrigemEstado { get; set; }

        [StringLength(10)]
        public string? OrigemCep { get; set; }

        [StringLength(200)]
        [Display(Name = "Endereço de destino")]
        public string? DestinoEndereco { get; set; }

        [StringLength(20)]
        public string? DestinoNumero { get; set; }

        [StringLength(80)]
        public string? DestinoBairro { get; set; }

        [StringLength(80)]
        public string? DestinoCidade { get; set; }

        [StringLength(2)]
        public string? DestinoEstado { get; set; }

        [StringLength(10)]
        public string? DestinoCep { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        [Range(typeof(decimal), "0.01", "20", ParseLimitsInInvariantCulture = true, ErrorMessage = "Informe uma altura entre 0,01 e 20 metros.")]
        public decimal? AlturaCargaMetros { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        [Range(typeof(decimal), "0.01", "20", ParseLimitsInInvariantCulture = true, ErrorMessage = "Informe uma largura entre 0,01 e 20 metros.")]
        public decimal? LarguraCargaMetros { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        [Range(typeof(decimal), "0.01", "30", ParseLimitsInInvariantCulture = true, ErrorMessage = "Informe um comprimento entre 0,01 e 30 metros.")]
        public decimal? ComprimentoCargaMetros { get; set; }

        [StringLength(50)]
        public string TipoCarga { get; set; } = "Carga Geral Seca";

        [Column(TypeName = "decimal(18,2)")]
        [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ErrorMessage = "Informe um valor declarado entre 0 e 100.000.000 reais.")]
        [Display(Name = "Valor Total (R$)")]
        public decimal ValorTotal { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ErrorMessage = "Informe um peso entre 0,01 e 1.000.000 kg.")]
        [Display(Name = "Peso (Kg)")]
        public decimal PesoKg { get; set; } = 1.0m;

        [Display(Name = "Data do Pedido")]
        public DateTime DataPedido { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Registrado"; 
        // Valores: "Registrado", "EmProcessamento", "ComEntregaAtribuida", "Concluido", "Cancelado"

        // Relação 1:0..1 com Entrega
        public Entrega? Entrega { get; set; }
    }
}
