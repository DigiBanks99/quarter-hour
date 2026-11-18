// Grouping a day's activities into entries by dragging one onto another (HTML drag and drop). Blazor owns the rows;
// this module only marks which rows a dragged activity may join, shows why the others refuse, and reports a drop.
// Rows say what they are with data-target ("piece:<id>", "entry:<id>" or "out" for the take-out zone); handles say
// what they drag with data-source (a piece id).

export function create(root, dotnet) {
    return new Grouping(root, dotnet);
}

const IDLE_HINT = 'Drop it on highlighted work to group them into one entry.';

class Grouping {
    constructor(root, dotnet) {
        this.root = root;
        this.dotnet = dotnet;
        this.allowed = {};
        this.reasons = {};
        this.dragging = null;
        this.lit = null;
        this.listeners = [];
        this.listen('dragstart', event => this.dragStart(event));
        this.listen('dragover', event => this.dragOver(event));
        this.listen('drop', event => this.drop(event));
        this.listen('dragend', () => this.endDrag());
    }

    listen(type, handler) {
        this.root.addEventListener(type, handler);
        this.listeners.push(() => this.root.removeEventListener(type, handler));
    }

    // allowed: source → targets it may be dropped on; reasons: source → target → why not.
    set(allowed, reasons) {
        this.allowed = allowed || {};
        this.reasons = reasons || {};
    }

    accepts(target) {
        return !!target && (this.allowed[this.dragging] || []).includes(target.dataset.target);
    }

    dragStart(event) {
        const handle = event.target.closest('[data-source]');
        if (!handle) {
            return;
        }

        this.dragging = handle.dataset.source;
        event.dataTransfer.setData('text/plain', this.dragging);
        event.dataTransfer.effectAllowed = 'move';
        const row = handle.closest('tr');
        if (row) {
            event.dataTransfer.setDragImage(row, 16, row.offsetHeight / 2);
        }

        this.root.classList.add('qh-grouping');
        this.root.classList.toggle('qh-grouping-member', (this.allowed[this.dragging] || []).includes('out'));
        for (const target of this.root.querySelectorAll('[data-target]')) {
            const ok = this.accepts(target);
            target.classList.toggle('qh-group-on', ok);
            target.classList.toggle('qh-group-off', !ok && !this.isSelf(target));
        }

        this.hint(IDLE_HINT);
    }

    isSelf(target) {
        return target.dataset.target === `piece:${this.dragging}`;
    }

    dragOver(event) {
        if (!this.dragging) {
            return;
        }

        const target = event.target.closest('[data-target]');
        if (this.accepts(target)) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            this.highlight(target);
            this.hint(target.dataset.target === 'out' ? 'Drop to take it out of its entry.' : 'Drop to group them.');
            return;
        }

        this.highlight(null);
        const reason = target && !this.isSelf(target) ? (this.reasons[this.dragging] || {})[target.dataset.target] : null;
        this.hint(reason ? `Can't group with this: ${reason}.` : IDLE_HINT);
    }

    drop(event) {
        const target = event.target.closest('[data-target]');
        const source = this.dragging;
        const ok = this.accepts(target);
        this.endDrag();
        if (!ok || !source) {
            return;
        }

        event.preventDefault();
        this.dotnet.invokeMethodAsync('OnGroup', source, target.dataset.target);
    }

    highlight(target) {
        if (this.lit === target) {
            return;
        }

        this.lit?.classList.remove('qh-group-over');
        this.lit = target;
        target?.classList.add('qh-group-over');
    }

    hint(text) {
        const element = this.root.querySelector('[data-group-hint]');
        if (element) {
            element.textContent = text;
        }
    }

    endDrag() {
        this.dragging = null;
        this.highlight(null);
        const hint = this.root.querySelector('[data-group-hint]');
        if (hint && hint.dataset.groupHint) {
            hint.textContent = hint.dataset.groupHint;
        }

        this.root.classList.remove('qh-grouping', 'qh-grouping-member');
        for (const target of this.root.querySelectorAll('.qh-group-on, .qh-group-off')) {
            target.classList.remove('qh-group-on', 'qh-group-off');
        }
    }

    dispose() {
        this.endDrag();
        this.listeners.forEach(remove => remove());
    }
}
