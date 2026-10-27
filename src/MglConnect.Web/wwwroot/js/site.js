// Funções utilitárias do M.G.L Connect

// 1. Busca Automática de Endereço via API ViaCEP
function buscarCep(cepInputId, logradouroId, bairroId, cidadeId, ufId) {
    const cepElement = document.getElementById(cepInputId);
    if (!cepElement) return;

    cepElement.addEventListener('blur', function() {
        const cep = this.value.replace(/\D/g, '');
        if (cep.length === 8) {
            // Indicador visual de carregamento
            cepElement.classList.add('is-loading');

            fetch(`https://viacep.com.br/ws/${cep}/json/`)
                .then(response => response.json())
                .then(data => {
                    cepElement.classList.remove('is-loading');
                    if (!data.erro) {
                        const elLog = document.getElementById(logradouroId);
                        const elBairro = document.getElementById(bairroId);
                        const elCidade = document.getElementById(cidadeId);
                        const elUf = document.getElementById(ufId);

                        if (elLog) elLog.value = data.logradouro || '';
                        if (elBairro) elBairro.value = data.bairro || '';
                        if (elCidade) elCidade.value = data.localidade || '';
                        if (elUf) elUf.value = data.uf || '';
                    } else {
                        alert('CEP não encontrado na base dos Correios/ViaCEP.');
                    }
                })
                .catch(err => {
                    cepElement.classList.remove('is-loading');
                    console.error('Erro na consulta ViaCEP:', err);
                });
        }
    });
}

// 2. Copiar Código de Rastreamento com Feedback
function copiarCodigoRastreio(codigo, buttonElement) {
    if (!navigator.clipboard) {
        // Fallback
        const temp = document.createElement('input');
        temp.value = codigo;
        document.body.appendChild(temp);
        temp.select();
        document.execCommand('copy');
        document.body.removeChild(temp);
    } else {
        navigator.clipboard.writeText(codigo);
    }

    if (buttonElement) {
        const originalHtml = buttonElement.innerHTML;
        buttonElement.innerHTML = '<i class="bi bi-check2"></i> Copiado!';
        buttonElement.classList.remove('btn-outline-primary', 'btn-outline-secondary');
        buttonElement.classList.add('btn-success');

        setTimeout(() => {
            buttonElement.innerHTML = originalHtml;
            buttonElement.classList.remove('btn-success');
            buttonElement.classList.add('btn-outline-primary');
        }, 2000);
    }
}
