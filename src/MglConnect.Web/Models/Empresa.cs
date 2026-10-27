using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models
{
    [Table("Empresas")]
    public class Empresa
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required(ErrorMessage = "O nome da empresa é obrigatório.")]
        [StringLength(150)]
        public string Nome { get; set; } = string.Empty;

        [Required(ErrorMessage = "O CNPJ é obrigatório.")]
        [StringLength(20)]
        public string Cnpj { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string PlanoAssinatura { get; set; } = "SaaS Enterprise 3PL";

        public bool Ativo { get; set; } = true;

        public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    }
}
