using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("Usuarios")]
    public class Usuario : ITenantOwned
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── Multi-Tenancy ──
        public Guid TenantId { get; set; } = Guid.Empty;

        [Required(ErrorMessage = "O nome é obrigatório.")]
        [StringLength(100)]
        public string Nome { get; set; } = string.Empty;

        [Required(ErrorMessage = "O login é obrigatório.")]
        [StringLength(50)]
        public string Login { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string SenhaHash { get; set; } = string.Empty;

        [Required(ErrorMessage = "O perfil é obrigatório.")]
        [StringLength(30)]
        public string Perfil { get; set; } = "Admin"; // "Admin", "Motorista", "Cliente"

        [StringLength(20)]
        public string? Telefone { get; set; }

        [StringLength(14)]
        public string? Cpf { get; set; }

        [Column(TypeName = "date")]
        [Display(Name = "Data de Nascimento")]
        public DateTime? DataNascimento { get; set; }

        public bool Ativo { get; set; } = true;

        public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

        // Vínculo opcional com Cliente corporativo (para perfis do tipo "Cliente")
        public int? ClienteId { get; set; }

        [ForeignKey("ClienteId")]
        public Cliente? Cliente { get; set; }
    }
}
