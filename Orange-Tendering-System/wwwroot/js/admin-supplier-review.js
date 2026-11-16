(() => {
  const dialog = document.getElementById('supplier-document-preview');
  if (!dialog) return;

  const frame = dialog.querySelector('iframe');
  const title = dialog.querySelector('#supplier-document-preview-title');
  const openLink = dialog.querySelector('[data-document-open]');
  let opener;

  document.querySelectorAll('[data-document-preview]').forEach(button => {
    button.addEventListener('click', () => {
      opener = button;
      title.textContent = button.dataset.documentName || 'Supplier document';
      openLink.href = button.dataset.documentPreview;
      frame.src = button.dataset.documentPreview;
      dialog.showModal();
    });
  });

  dialog.querySelectorAll('[data-document-close]').forEach(button => {
    button.addEventListener('click', () => dialog.close());
  });
  dialog.addEventListener('click', event => {
    if (event.target === dialog) dialog.close();
  });
  dialog.addEventListener('close', () => {
    frame.removeAttribute('src');
    opener?.focus();
  });
})();
