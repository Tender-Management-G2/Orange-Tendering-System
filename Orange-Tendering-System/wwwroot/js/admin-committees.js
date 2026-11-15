"use strict";
document.querySelectorAll('[data-committee-form]').forEach(form => {
  const selects = [...form.querySelectorAll('select')];
  function validate() {
    const chosen = selects.map(select => select.value);
    selects.forEach(select => {
      select.setCustomValidity(select.value && chosen.filter(id => id === select.value).length > 1
        ? 'Choose a different person for each seat.' : '');
    });
  }
  selects.forEach(select => select.addEventListener('change', validate));
  form.addEventListener('submit', validate);
});
