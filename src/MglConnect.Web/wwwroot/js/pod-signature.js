// Signature capture must work independently of map/CDN availability.
document.addEventListener('DOMContentLoaded', () => {
    const canvas = document.getElementById('sigCanvasNav');
    const form = document.getElementById('formPODNavegacao');
    const field = document.getElementById('assinaturaBase64Nav');
    const clear = document.getElementById('btnClearSigNav');
    if (!canvas || !form || !field || !clear) return;
    const context = canvas.getContext('2d');
    if (!context) return;
    let drawing = false;
    let ink = false;
    context.strokeStyle = '#0B132B';
    context.lineWidth = 2.5;
    context.lineCap = 'round';
    const position = event => {
        const bounds = canvas.getBoundingClientRect();
        return { x: (event.clientX - bounds.left) * canvas.width / bounds.width,
            y: (event.clientY - bounds.top) * canvas.height / bounds.height };
    };
    canvas.addEventListener('pointerdown', event => {
        drawing = true;
        const point = position(event);
        context.beginPath();
        context.moveTo(point.x, point.y);
        canvas.setPointerCapture(event.pointerId);
        event.preventDefault();
    });
    canvas.addEventListener('pointermove', event => {
        if (!drawing) return;
        const point = position(event);
        context.lineTo(point.x, point.y);
        context.stroke();
        ink = true;
    });
    const end = () => { drawing = false; };
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', end);
    clear.addEventListener('click', () => {
        context.clearRect(0, 0, canvas.width, canvas.height);
        ink = false;
        field.value = '';
    });
    const prepare = () => { field.value = ink ? canvas.toDataURL('image/png') : ''; };
    form.addEventListener('mgl:prepare-pod', prepare);
    form.addEventListener('submit', prepare);
});
