using System;
using System.ComponentModel.DataAnnotations;

namespace MglConnect.Web.ViewModels
{
    public class NovaEntregaViewModel
    {
        [Required(ErrorMessage = "Selecione o pedido a ser despachado.")]
        [Display(Name = "Pedido")]
        public int PedidoId { get; set; }

        [Display(Name = "Motorista Responsável")]
        public int? MotoristaId { get; set; }

        [Display(Name = "Veículo Pesado / Caminhão")]
        public int? VeiculoId { get; set; }

        [Required(ErrorMessage = "Nome do destinatário é obrigatório.")]
        [StringLength(100)]
        [Display(Name = "Destinatário")]
        public string DestinatarioNome { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "CPF / CNPJ do Destinatário")]
        public string? DestinatarioDocumento { get; set; }

        [Required(ErrorMessage = "CEP de destino é obrigatório.")]
        [StringLength(10)]
        [Display(Name = "CEP")]
        public string CepDestino { get; set; } = string.Empty;

        [Required(ErrorMessage = "Logradouro é obrigatório.")]
        [StringLength(200)]
        [Display(Name = "Endereço")]
        public string EnderecoDestino { get; set; } = string.Empty;

        [Required(ErrorMessage = "Número é obrigatório.")]
        [StringLength(20)]
        [Display(Name = "Número")]
        public string NumeroDestino { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Complemento")]
        public string? ComplementoDestino { get; set; }

        [Required(ErrorMessage = "Bairro é obrigatório.")]
        [StringLength(80)]
        [Display(Name = "Bairro")]
        public string BairroDestino { get; set; } = string.Empty;

        [Required(ErrorMessage = "Cidade é obrigatória.")]
        [StringLength(80)]
        [Display(Name = "Cidade")]
        public string CidadeDestino { get; set; } = string.Empty;

        [Required(ErrorMessage = "Estado (UF) é obrigatório.")]
        [StringLength(2)]
        [Display(Name = "UF")]
        public string EstadoDestino { get; set; } = "SP";

        [Display(Name = "Prazo Estimado (em dias)")]
        [Range(1, 60)]
        public int PrevisaoDias { get; set; } = 3;

        [StringLength(500)]
        [Display(Name = "Instruções / Observações")]
        public string? Observacoes { get; set; }

        // Parâmetros de Roteamento de Carga Pesada
        [Display(Name = "Altura da Carga (m)")]
        public decimal AlturaMetros { get; set; } = 4.20m;

        [Display(Name = "Largura da Carga (m)")]
        public decimal LarguraMetros { get; set; } = 2.60m;

        [Display(Name = "Peso Bruto (toneladas)")]
        public decimal PesoToneladas { get; set; } = 25.50m;

        [Display(Name = "Quantidade de Eixos")]
        public int QtdEixos { get; set; } = 5;

        [Display(Name = "Tipo de Carga")]
        public string TipoCarga { get; set; } = "Carga Geral Seca";

        [Display(Name = "Restrição de Rota para Caminhões")]
        public bool RotaRestritaCaminhao { get; set; } = true;
    }
}
