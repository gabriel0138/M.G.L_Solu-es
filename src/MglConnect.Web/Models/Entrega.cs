using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("Entregas")]
    public class Entrega : ITenantOwned
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── Multi-Tenancy ──
        public Guid TenantId { get; set; } = Guid.Empty;

        [Required]
        [Display(Name = "Pedido")]
        public int PedidoId { get; set; }

        [ForeignKey("PedidoId")]
        public Pedido? Pedido { get; set; }

        [Display(Name = "Motorista Responsável")]
        public int? MotoristaId { get; set; }

        [ForeignKey("MotoristaId")]
        public Usuario? Motorista { get; set; }

        // Veículo Pesado Designado para Rota
        [Display(Name = "Veículo Pesado / Frota")]
        public int? VeiculoId { get; set; }

        [ForeignKey("VeiculoId")]
        public Veiculo? Veiculo { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Código de Rastreio")]
        public string CodigoRastreio { get; set; } = string.Empty;

        [Required(ErrorMessage = "Destinatário é obrigatório.")]
        [StringLength(100)]
        [Display(Name = "Nome do Destinatário")]
        public string DestinatarioNome { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Documento Destinatário")]
        public string? DestinatarioDocumento { get; set; }

        [Required(ErrorMessage = "Endereço de destino é obrigatório.")]
        [StringLength(200)]
        [Display(Name = "Logradouro")]
        public string EnderecoDestino { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Número")]
        public string? NumeroDestino { get; set; }

        [StringLength(100)]
        [Display(Name = "Complemento")]
        public string? ComplementoDestino { get; set; }

        [StringLength(80)]
        [Display(Name = "Bairro")]
        public string? BairroDestino { get; set; }

        [Required(ErrorMessage = "Cidade de destino é obrigatória.")]
        [StringLength(80)]
        [Display(Name = "Cidade")]
        public string CidadeDestino { get; set; } = string.Empty;

        [Required(ErrorMessage = "Estado de destino é obrigatório.")]
        [StringLength(2)]
        [Display(Name = "UF")]
        public string EstadoDestino { get; set; } = string.Empty;

        [Required(ErrorMessage = "CEP é obrigatório.")]
        [StringLength(10)]
        [Display(Name = "CEP")]
        public string CepDestino { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        [Display(Name = "Status da Entrega")]
        public string StatusEntrega { get; set; } = "Pendente"; 
        // "Pendente", "Em transporte", "Entregue", "Cancelada"

        public bool AtribuicaoAtiva { get; set; }
        public int Versao { get; set; }

        [Display(Name = "Data de Emissão")]
        public DateTime DataCriacao { get; set; } = DateTime.UtcNow;

        [Display(Name = "Previsão de Entrega")]
        public DateTime? DataPrevisaoEntrega { get; set; }

        [Display(Name = "Data da Entrega Realizada")]
        public DateTime? DataEntregaRealizada { get; set; }

        [StringLength(500)]
        [Display(Name = "Observações")]
        public string? Observacoes { get; set; }

        // --- ROTEAMENTO DE VEÍCULOS PESADOS E CAMINHÕES (HEAVY TRUCK ROUTING) ---
        [Column(TypeName = "decimal(5,2)")]
        [Display(Name = "Altura da Carga (metros)")]
        public decimal AlturaMetros { get; set; } = 4.20m;

        [Column(TypeName = "decimal(5,2)")]
        [Display(Name = "Largura da Carga (metros)")]
        public decimal LarguraMetros { get; set; } = 2.60m;

        [Column(TypeName = "decimal(6,2)")]
        [Display(Name = "Peso Bruto (toneladas)")]
        public decimal PesoToneladas { get; set; } = 25.50m;

        [Display(Name = "Quantidade de Eixos")]
        public int QtdEixos { get; set; } = 5;

        [StringLength(50)]
        [Display(Name = "Classificação de Carga")]
        public string TipoCarga { get; set; } = "Carga Geral Seca";

        [Display(Name = "Requer Rota com Restrição para Caminhões")]
        public bool RotaRestritaCaminhao { get; set; } = true;

        // --- COMPROVANTE DIGITAL DE ENTREGA (PROOF OF DELIVERY - POD) ---
        [StringLength(100)]
        [Display(Name = "Nome de Quem Recebeu")]
        public string? NomeRecebedor { get; set; }

        [StringLength(30)]
        [Display(Name = "Documento de Quem Recebeu (RG/CPF)")]
        public string? DocumentoRecebedor { get; set; }

        [Display(Name = "Data e Hora do Recebimento")]
        public DateTime? DataHoraRecebimento { get; set; }

        // Assinatura digital do recebedor desenhada na tela em Base64 Data URL
        public string? AssinaturaDigitalBase64 { get; set; }

        [StringLength(255)]
        [Display(Name = "Caminho da Foto do Comprovante")]
        public string? FotoComprovantePath { get; set; }

        // Linha do tempo de rastreamento (Mercado Livre Timeline)
        public ICollection<EventoRastreio> EventosRastreio { get; set; } = new List<EventoRastreio>();
    }
}
