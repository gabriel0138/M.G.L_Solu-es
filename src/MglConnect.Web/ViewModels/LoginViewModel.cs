using System.ComponentModel.DataAnnotations;

namespace MglConnect.Web.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Informe seu usuário ou login.")]
        public string Login { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe sua senha.")]
        [DataType(DataType.Password)]
        public string Senha { get; set; } = string.Empty;

        public bool LembrarMe { get; set; }
        public string? ReturnUrl { get; set; }
    }
}
