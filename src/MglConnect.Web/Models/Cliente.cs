using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("Clientes")]
    public class Cliente : ITenantOwned
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── Multi-Tenancy ──
        public Guid TenantId { get; set; } = Guid.Empty;

        [Required(ErrorMessage = "A Razão Social é obrigatória.")]
        [StringLength(150)]
        [Display(Name = "Razão Social")]
        public string RazaoSocial { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Nome Fantasia")]
        public string? NomeFantasia { get; set; }

        [Required(ErrorMessage = "O CNPJ é obrigatório.")]
        [StringLength(20)]
        public string Cnpj { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        [StringLength(100)]
        public string? Email { get; set; }

        [Required(ErrorMessage = "O telefone é obrigatório.")]
        [StringLength(20)]
        public string Telefone { get; set; } = string.Empty;

        [StringLength(10)]
        public string? Cep { get; set; }

        [StringLength(150)]
        public string? Logradouro { get; set; }

        [StringLength(20)]
        public string? Numero { get; set; }

        [StringLength(100)]
        public string? Complemento { get; set; }

        [StringLength(80)]
        public string? Bairro { get; set; }

        [StringLength(80)]
        public string? Cidade { get; set; }

        [StringLength(2)]
        public string? Estado { get; set; }

        public bool Ativo { get; set; } = true;

        public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

        // Navegação
        public ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
    }
}
