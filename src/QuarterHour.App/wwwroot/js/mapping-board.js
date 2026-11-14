// The mapping board's links: SVG curves from each group's handle to its category, drawn over the two columns.
// Groups can be linked by dragging the card onto a category (HTML drag and drop) or by pulling a line from the
// card's handle (pointer events). Blazor owns the cards; this module only owns the SVG inside the overlay element.
// A board may say which targets each card can go to; the others stay dark while that card is dragged.

const SVG = 'http://www.w3.org/2000/svg';

export function create(root, overlay, dotnet) {
    return new Board(root, overlay, dotnet);
}

class Board {
    constructor(root, overlay, dotnet) {
        this.root = root;
        this.dotnet = dotnet;
        this.links = [];
        this.selected = null;
        this.allowed = null;
        this.frame = 0;
        this.svg = document.createElementNS(SVG, 'svg');
        this.svg.classList.add('qh-links');
        this.group = document.createElementNS(SVG, 'g');
        this.svg.appendChild(this.group);
        overlay.appendChild(this.svg);

        this.listeners = [];
        this.listen(window, 'resize', () => this.schedule());
        this.listen(root, 'scroll', () => this.schedule(), true);
        this.resizeObserver = new ResizeObserver(() => this.schedule());
        this.resizeObserver.observe(root);

        this.listen(root, 'pointerdown', event => this.startLine(event));
        this.listen(root, 'dragstart', event => this.dragStart(event));
        this.listen(root, 'dragover', event => this.dragOver(event));
        this.listen(root, 'dragleave', event => this.dragLeave(event));
        this.listen(root, 'drop', event => this.drop(event));
        this.listen(root, 'dragend', () => this.endDrag());
        this.listen(this.svg, 'click', event => {
            const path = event.target.closest('path[data-source]');
            if (path) {
                this.dotnet.invokeMethodAsync('OnLinkClicked', path.dataset.source);
            }
        });
    }

    listen(target, type, handler, capture = false) {
        target.addEventListener(type, handler, { capture, passive: type === 'scroll' });
        this.listeners.push(() => target.removeEventListener(type, handler, { capture }));
    }

    setLinks(links, selected, allowed) {
        this.links = links || [];
        this.selected = selected || null;
        this.allowed = allowed || null;
        this.schedule();
    }

    accepts(source, target) {
        return !target || !this.allowed || (this.allowed[source] || []).includes(target.dataset.target);
    }

    // Dims the targets a card can't go to while it is dragged or a line is pulled from it.
    markAllowed(source) {
        for (const target of this.root.querySelectorAll('[data-target]')) {
            target.classList.toggle('qh-target-off', source !== null && !this.accepts(source, target));
        }
    }

    schedule() {
        if (this.frame) {
            return;
        }

        this.frame = requestAnimationFrame(() => {
            this.frame = 0;
            this.draw();
        });
    }

    draw() {
        const bounds = this.root.getBoundingClientRect();
        this.svg.setAttribute('width', bounds.width);
        this.svg.setAttribute('height', bounds.height);
        this.svg.setAttribute('viewBox', `0 0 ${bounds.width} ${bounds.height}`);

        const fragment = document.createDocumentFragment();
        for (const link of this.links) {
            const from = this.port('source', link.source);
            const to = this.port('target', link.target);
            if (!from || !to) {
                continue;
            }

            const a = this.anchor(from, bounds);
            const b = this.anchor(to, bounds);
            const path = document.createElementNS(SVG, 'path');
            path.setAttribute('d', curve(a, b));
            path.setAttribute('stroke', link.color || '#64748b');
            path.dataset.source = link.source;
            path.classList.add(`qh-link-${link.kind}`);
            if (a.clamped || b.clamped) {
                path.classList.add('qh-link-offscreen');
            }

            if (this.selected) {
                path.classList.add(link.source === this.selected ? 'qh-link-selected' : 'qh-link-dim');
            }

            const title = document.createElementNS(SVG, 'title');
            title.textContent = link.title || '';
            path.appendChild(title);
            fragment.appendChild(path);
        }

        this.group.replaceChildren(fragment);
    }

    port(kind, key) {
        return this.root.querySelector(`[data-${kind}-port="${CSS.escape(key)}"]`);
    }

    // The centre of a handle, kept inside its (scrolling) column so off-screen links still point the right way.
    anchor(element, bounds) {
        const rect = element.getBoundingClientRect();
        const column = element.closest('.qh-col');
        const columnRect = column ? column.getBoundingClientRect() : bounds;
        let y = rect.top + rect.height / 2;
        let clamped = false;
        if (y < columnRect.top + 6) {
            y = columnRect.top + 6;
            clamped = true;
        } else if (y > columnRect.bottom - 6) {
            y = columnRect.bottom - 6;
            clamped = true;
        }

        return { x: rect.left + rect.width / 2 - bounds.left, y: y - bounds.top, clamped };
    }

    startLine(event) {
        const handle = event.target.closest('[data-source-port]');
        if (!handle || event.button !== 0) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        const bounds = this.root.getBoundingClientRect();
        const start = this.anchor(handle, bounds);
        const source = handle.dataset.sourcePort;
        const temp = document.createElementNS(SVG, 'path');
        temp.classList.add('qh-link-temp');
        this.svg.appendChild(temp);
        this.root.classList.add('qh-linking');
        handle.setPointerCapture(event.pointerId);

        this.markAllowed(source);
        const move = moveEvent => {
            const end = { x: moveEvent.clientX - bounds.left, y: moveEvent.clientY - bounds.top };
            temp.setAttribute('d', curve(start, end));
            const over = this.targetAt(moveEvent.clientX, moveEvent.clientY);
            this.highlight(this.accepts(source, over) ? over : null);
        };

        const finish = upEvent => {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', finish);
            handle.removeEventListener('pointercancel', finish);
            if (handle.hasPointerCapture(event.pointerId)) {
                handle.releasePointerCapture(event.pointerId);
            }

            temp.remove();
            this.root.classList.remove('qh-linking');
            this.markAllowed(null);
            const target = upEvent.type === 'pointerup' ? this.targetAt(upEvent.clientX, upEvent.clientY) : null;
            this.highlight(null);
            if (target && this.accepts(source, target)) {
                this.dotnet.invokeMethodAsync('OnLink', source, target.dataset.target);
            }
        };

        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', finish);
        handle.addEventListener('pointercancel', finish);
    }

    targetAt(x, y) {
        const element = document.elementFromPoint(x, y);
        return element ? element.closest('[data-target]') : null;
    }

    highlight(target) {
        if (this.lit === target) {
            return;
        }

        if (this.lit) {
            this.lit.classList.remove('qh-drop');
        }

        this.lit = target;
        if (target) {
            target.classList.add('qh-drop');
        }
    }

    dragStart(event) {
        const card = event.target.closest('[data-source]');
        if (!card || card.getAttribute('draggable') === 'false') {
            return;
        }

        event.dataTransfer.setData('application/x-quarterhour-group', card.dataset.source);
        event.dataTransfer.setData('text/plain', card.dataset.source);
        event.dataTransfer.effectAllowed = 'link';
        this.dragging = card.dataset.source;
        this.root.classList.add('qh-dragging');
        this.markAllowed(this.dragging);
    }

    dragOver(event) {
        const target = event.target.closest('[data-target]');
        if (!target || !this.dragging || !this.accepts(this.dragging, target)) {
            return;
        }

        event.preventDefault();
        event.dataTransfer.dropEffect = 'link';
        this.highlight(target);
    }

    dragLeave(event) {
        const target = event.target.closest('[data-target]');
        if (target && !target.contains(event.relatedTarget)) {
            this.highlight(null);
        }
    }

    drop(event) {
        const target = event.target.closest('[data-target]');
        const source = this.dragging || event.dataTransfer.getData('application/x-quarterhour-group');
        this.endDrag();
        if (!target || !source || !this.accepts(source, target)) {
            return;
        }

        event.preventDefault();
        this.dotnet.invokeMethodAsync('OnLink', source, target.dataset.target);
    }

    endDrag() {
        this.dragging = null;
        this.root.classList.remove('qh-dragging');
        this.markAllowed(null);
        this.highlight(null);
    }

    dispose() {
        cancelAnimationFrame(this.frame);
        this.listeners.forEach(remove => remove());
        this.resizeObserver.disconnect();
        this.svg.remove();
    }
}

function curve(a, b) {
    const bend = Math.max(48, Math.abs(b.x - a.x) * 0.5);
    return `M ${a.x} ${a.y} C ${a.x + bend} ${a.y}, ${b.x - bend} ${b.y}, ${b.x} ${b.y}`;
}
