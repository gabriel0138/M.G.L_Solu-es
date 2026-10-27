using System.ComponentModel.DataAnnotations;

namespace MglConnect.Web.ViewModels
{
    public class NovoCheckpointViewModel
    {
        [Required]
        public int EntregaId { get; set; }

        public string? CodigoRastreio { get; set; }

        [Required(ErrorMessage = "Informe o título do evento.")]
        [StringLength(100)]
        [Display(Name = "Título do Evento")]
        public string Titulo { get; set; } = string.Empty;

        [StringLength(255)]
        [Display(Name = "Descrição Detalhada")]
        public string? Descricao { get; set; }

        [Required(ErrorMessage = "Informe a localidade ou centro de distribuição.")]
        [StringLength(100)]
        [Display(Name = "Localidade / Hub")]
        public string Localidade { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Novo Status da Entrega")]
        public string StatusAssociado { get; set; } = "Em transporte";

        [Display(Name = "Ícone")]
        public string Icone { get; set; } = "bi-truck";
    }
}
