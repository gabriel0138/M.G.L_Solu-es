using System;
using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("Veiculos")]
    public class Veiculo : ITenantOwned
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── Multi-Tenancy ──
        public Guid TenantId { get; set; } = Guid.Empty;

        [Required(ErrorMessage = "A placa do veículo é obrigatória.")]
        [StringLength(10)]
        [Display(Name = "Placa")]
        public string Placa { get; set; } = string.Empty;

        [Required(ErrorMessage = "O modelo é obrigatório.")]
        [StringLength(80)]
        [Display(Name = "Modelo")]
        public string Modelo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Capacidade de Carga (kg)")]
        public decimal CapacidadeCargaKg { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        [Display(Name = "Altura (metros)")]
        public decimal AlturaMetros { get; set; } = 4.20m;

        [Column(TypeName = "decimal(5,2)")]
        [Display(Name = "Largura (metros)")]
        public decimal LarguraMetros { get; set; } = 2.60m;

        [Column(TypeName = "decimal(5,2)")]
        [Display(Name = "Comprimento (metros)")]
        public decimal ComprimentoMetros { get; set; } = 18.60m;

        [Column(TypeName = "decimal(6,2)")]
        [Display(Name = "Peso Bruto Total (PBT em toneladas)")]
        public decimal PesoToneladas { get; set; } = 29.50m;

        [Display(Name = "Quantidade de Eixos")]
        public int QtdEixos { get; set; } = 6;

        [Required]
        [StringLength(50)]
        [Display(Name = "Tipo de Carga Habilitada")]
        public string TipoCarga { get; set; } = "Carga Geral Seca"; 
        // Ex: "Carga Geral Seca", "Produtos Perigosos", "Frigorífico", "Carga Indivisível"

        public bool Ativo { get; set; } = true;
    }
}
