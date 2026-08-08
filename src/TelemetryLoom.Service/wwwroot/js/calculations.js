(() => {
    "use strict";

    const formula = document.querySelector("[data-formula-input]");
    if (!formula) {
        return;
    }

    document.querySelectorAll("[data-insert-operand]").forEach(button => {
        button.addEventListener("click", () => {
            const key = button.dataset.insertOperand;
            if (!key) {
                return;
            }

            const start = formula.selectionStart ?? formula.value.length;
            const end = formula.selectionEnd ?? start;
            formula.setRangeText(key, start, end, "end");
            formula.focus();
        });
    });
})();
