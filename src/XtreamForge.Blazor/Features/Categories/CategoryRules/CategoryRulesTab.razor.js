const dragDropState = new WeakMap();

export function initializeRuleDragDrop(table, dotNetHelper) {
    disposeRuleDragDrop(table);

    if (!table || !dotNetHelper) {
        return;
    }

    const handles = Array.from(table.querySelectorAll('[data-drag-handle="true"]'));
    const dataRows = Array.from(table.querySelectorAll('tbody tr[data-rule-id]'));
    const tailZone = table.querySelector('tbody tr.rule-drop-zone[data-drop-index]');

    if (handles.length === 0 || dataRows.length === 0) {
        return;
    }

    let draggedRuleId = null;

    const clearTargets = () => {
        dataRows.forEach(row => {
            row.classList.remove('dragging-row', 'drop-target-before', 'drop-target-after');
        });

        tailZone?.classList.remove('active');
    };

    const resolveDropIndex = (row, clientY) => {
        const rowIndex = Number(row.dataset.dropIndex ?? '-1');
        if (rowIndex < 0) {
            return null;
        }

        const rect = row.getBoundingClientRect();
        return clientY <= rect.top + (rect.height / 2)
            ? rowIndex
            : rowIndex + 1;
    };

    const setTarget = (row, dropIndex) => {
        clearTargets();

        const rowIndex = Number(row.dataset.dropIndex ?? '-1');
        if (draggedRuleId === null || rowIndex < 0 || dropIndex === null) {
            return;
        }

        const draggedRow = dataRows.find(candidate => Number(candidate.dataset.ruleId) === draggedRuleId);
        draggedRow?.classList.add('dragging-row');

        if (dropIndex <= rowIndex) {
            row.classList.add('drop-target-before');
            return;
        }

        row.classList.add('drop-target-after');
    };

    const handleDragStart = (event) => {
        const row = event.currentTarget.closest('tr[data-rule-id]');
        draggedRuleId = row ? Number(row.dataset.ruleId) : null;

        if (draggedRuleId === null || Number.isNaN(draggedRuleId)) {
            return;
        }

        event.dataTransfer?.setData('text/plain', String(draggedRuleId));
        if (event.dataTransfer) {
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.dropEffect = 'move';
        }

        row.classList.add('dragging-row');
    };

    const handleDragEnd = () => {
        draggedRuleId = null;
        clearTargets();
    };

    const handleRowDragOver = (event) => {
        if (draggedRuleId === null) {
            return;
        }

        event.preventDefault();
        const row = event.currentTarget;
        setTarget(row, resolveDropIndex(row, event.clientY));
    };

    const handleRowDrop = async (event) => {
        if (draggedRuleId === null) {
            return;
        }

        event.preventDefault();
        const row = event.currentTarget;
        const dropIndex = resolveDropIndex(row, event.clientY);

        clearTargets();
        const capturedRuleId = draggedRuleId;
        draggedRuleId = null;

        if (dropIndex === null) {
            return;
        }

        await dotNetHelper.invokeMethodAsync('HandleRuleDropFromJs', capturedRuleId, dropIndex);
    };

    const handleTailDragOver = (event) => {
        if (draggedRuleId === null || !tailZone) {
            return;
        }

        event.preventDefault();
        clearTargets();
        const draggedRow = dataRows.find(candidate => Number(candidate.dataset.ruleId) === draggedRuleId);
        draggedRow?.classList.add('dragging-row');
        tailZone.classList.add('active');
    };

    const handleTailDrop = async (event) => {
        if (draggedRuleId === null || !tailZone) {
            return;
        }

        event.preventDefault();
        clearTargets();
        const capturedRuleId = draggedRuleId;
        draggedRuleId = null;
        await dotNetHelper.invokeMethodAsync('HandleRuleDropFromJs', capturedRuleId, Number(tailZone.dataset.dropIndex));
    };

    handles.forEach(handle => {
        handle.setAttribute('draggable', 'true');
        handle.draggable = true;
        handle.addEventListener('dragstart', handleDragStart);
        handle.addEventListener('dragend', handleDragEnd);
    });

    dataRows.forEach(row => {
        row.addEventListener('dragover', handleRowDragOver);
        row.addEventListener('drop', handleRowDrop);
    });

    tailZone?.addEventListener('dragover', handleTailDragOver);
    tailZone?.addEventListener('drop', handleTailDrop);

    dragDropState.set(table, () => {
        handles.forEach(handle => {
            handle.removeEventListener('dragstart', handleDragStart);
            handle.removeEventListener('dragend', handleDragEnd);
            handle.removeAttribute('draggable');
            handle.draggable = false;
        });

        dataRows.forEach(row => {
            row.removeEventListener('dragover', handleRowDragOver);
            row.removeEventListener('drop', handleRowDrop);
            row.classList.remove('dragging-row', 'drop-target-before', 'drop-target-after');
        });

        tailZone?.removeEventListener('dragover', handleTailDragOver);
        tailZone?.removeEventListener('drop', handleTailDrop);
        tailZone?.classList.remove('active');
    });
}

export function disposeRuleDragDrop(table) {
    const dispose = table ? dragDropState.get(table) : null;
    dispose?.();

    if (table) {
        dragDropState.delete(table);
    }
}
