// Capacity is an observed catalogue value, not a route or vehicle compliance approval.
(() => {
    const order = document.getElementById('selectPedidoDespacho');
    const vehicle = document.getElementById('selectVeiculoFrota');
    const box = document.getElementById('badgeSugestaoDespacho');
    const message = document.getElementById('textoSugestaoDespacho');
    if (!order || !vehicle || !box || !message) return;
    const format = value => value.toLocaleString('pt-BR', { maximumFractionDigits: 2 });
    function update() {
        const selectedOrder = order.options[order.selectedIndex];
        const selectedVehicle = vehicle.options[vehicle.selectedIndex];
        const weight = Number(selectedOrder?.dataset.peso);
        const capacity = Number(selectedVehicle?.dataset.capacidade);
        box.dataset.state = 'unknown';
        if (!selectedOrder?.value || !Number.isFinite(weight) || weight <= 0) {
            message.textContent = 'Selecione um pedido com peso informado para comparar a capacidade do veículo.';
        } else if (!selectedVehicle?.value || !Number.isFinite(capacity) || capacity <= 0) {
            message.textContent = `Carga: ${format(weight)} kg. Selecione um veículo com capacidade cadastrada para a comparação.`;
        } else if (weight > capacity) {
            box.dataset.state = 'warning';
            message.textContent = `Carga de ${format(weight)} kg excede a capacidade cadastrada de ${format(capacity)} kg. Escolha outro veículo.`;
        } else {
            box.dataset.state = 'within-capacity';
            message.textContent = `Carga: ${format(weight)} kg · Capacidade cadastrada: ${format(capacity)} kg. Confira também dimensões e condições da operação antes do despacho.`;
        }
    }
    order.addEventListener('change', update);
    vehicle.addEventListener('change', update);
    update();
})();
