using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("EventosRastreio")]
    public class EventoRastreio
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int EntregaId { get; set; }

        [ForeignKey("EntregaId")]
        public Entrega? Entrega { get; set; }

        [Required]
        [Display(Name = "Data e Hora")]
        public DateTime DataHora { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "O título do evento é obrigatório.")]
        [StringLength(100)]
        [Display(Name = "Título do Evento")]
        public string Titulo { get; set; } = string.Empty;

        [StringLength(255)]
        [Display(Name = "Descrição Detalhada")]
        public string? Descricao { get; set; }

        [Required(ErrorMessage = "A localidade é obrigatória.")]
        [StringLength(100)]
        [Display(Name = "Localidade / Unidade")]
        public string Localidade { get; set; } = string.Empty;

        [StringLength(30)]
        [Display(Name = "Status Vinculado")]
        public string StatusAssociado { get; set; } = "Pendente"; 
        // "Pendente", "Em transporte", "Entregue"

        [StringLength(50)]
        [Display(Name = "Ícone")]
        public string Icone { get; set; } = "bi-truck"; // Bootstrap icons (ex: bi-box-seam, bi-truck, bi-geo-alt, bi-check-circle-fill)
    }
}
