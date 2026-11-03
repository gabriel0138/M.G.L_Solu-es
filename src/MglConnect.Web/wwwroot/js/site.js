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

// 2. A confirmação só aparece depois que a área de transferência aceita o código.
async function copiarCodigoRastreio(codigo, buttonElement) {
    if (buttonElement?.dataset.copyState === 'pending') return false;
    const original = buttonElement ? {
        html: buttonElement.innerHTML, classes: buttonElement.className,
        title: buttonElement.getAttribute('title'), label: buttonElement.getAttribute('aria-label'),
        disabled: buttonElement.disabled
    } : null;
    if (buttonElement) {
        buttonElement.dataset.copyState = 'pending';
        buttonElement.disabled = true;
    }
    let copied = false;
    try {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(codigo);
            copied = true;
        } else {
            const previousFocus = document.activeElement;
            const temp = document.createElement('input');
            temp.value = codigo;
            temp.style.position = 'fixed';
            temp.style.left = '-9999px';
            document.body.appendChild(temp);
            try {
                temp.select();
                copied = document.execCommand('copy') === true;
            } finally {
                temp.remove();
                previousFocus?.focus();
            }
        }
    } catch {
        copied = false;
    }
    const message = copied ? 'Código copiado.' : 'Não foi possível copiar. Selecione o código e copie manualmente.';
    if (buttonElement) {
        buttonElement.disabled = original.disabled;
        buttonElement.dataset.copyState = copied ? 'copied' : 'failed';
        buttonElement.innerHTML = copied ? '<i class="bi bi-check2" aria-hidden="true"></i>' : '<i class="bi bi-exclamation-circle" aria-hidden="true"></i>';
        buttonElement.setAttribute('title', message);
        buttonElement.setAttribute('aria-label', message);
        setTimeout(() => {
            buttonElement.innerHTML = original.html;
            buttonElement.className = original.classes;
            for (const [name, value] of [['title', original.title], ['aria-label', original.label]]) {
                if (value === null) buttonElement.removeAttribute(name);
                else buttonElement.setAttribute(name, value);
            }
            delete buttonElement.dataset.copyState;
        }, 2500);
    }
    const host = document.getElementById('commandToasts');
    if (host) {
        const toast = document.createElement('div');
        toast.className = 'toast show';
        toast.setAttribute('role', 'status');
        const body = document.createElement('p');
        body.className = 'p-3 mb-0';
        body.textContent = message;
        toast.append(body);
        host.append(toast);
        setTimeout(() => toast.remove(), 4000);
    }
    return copied;
}
