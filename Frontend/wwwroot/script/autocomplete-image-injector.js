window.PathAutocompleteImageInjector = (function () {
    let observer = null;
    let imageMap = null;

    function updateImageMap(itemsWithImages) {
        imageMap = new Map(
            itemsWithImages
                .filter(item => item.imagePath && item.imagePath.length > 0)
                .map(item => [item.name, item.imagePath])
        );
    }

    function initPathAutocompleteImageInjector(itemsWithImages) {

        const container = document.getElementById('path-autocomplete-container');
        if (!container) {
            console.warn('Container path-autocomplete-container not found');
            return;
        }

        // Initialize or update image map
        updateImageMap(itemsWithImages);

        // Disconnect existing observer
        if (observer) {
            observer.disconnect();
        }

        // Observe the dropdown list for changes
        observer = new MutationObserver(() => {
            const dropdown = container.querySelector('.dropdown-menu');
            if (!dropdown) return;

            //console.log('Dropdown detected, processing items...');
            const items = dropdown.querySelectorAll('.dropdown-item');
            //console.log('Found', items.length, 'dropdown items');

            items.forEach(item => {
                if (item.dataset.imageProcessed) return;

                const text = item.textContent.trim();
                //console.log('Processing item:', text);
                const imagePath = imageMap.get(text);
                //console.log('Image path:', imagePath);

                if (imagePath) {
                    //console.log('Injecting image for:', text);
                    item.dataset.imageProcessed = 'true';

                    // Wrap the existing content
                    const wrapper = document.createElement('div');
                    wrapper.className = 'd-flex align-items-center';
                    wrapper.style.width = '100%';

                    // Create small thumbnail image
                    const smallImg = document.createElement('img');
                    smallImg.src = imagePath;
                    smallImg.alt = '';
                    smallImg.style.width = '24px';
                    smallImg.style.height = '24px';
                    smallImg.style.marginRight = '8px';
                    smallImg.style.objectFit = 'cover';
                    smallImg.style.cursor = 'pointer';

                    // Create hover preview that will be appended to body
                    const hoverPreview = document.createElement('div');
                    hoverPreview.style.cssText = `
                        position: fixed;
                        top: 10px;
                        left: 10px;
                        border: 2px solid #333;
                        box-shadow: 0 4px 8px rgba(0,0,0,0.3);
                        z-index: 99999;
                        display: none;
                        pointer-events: none;
                    `;

                    const largeImg = document.createElement('img');
                    largeImg.src = imagePath;
                    largeImg.alt = '';
                    largeImg.style.cssText = `
                        max-width: 90vw;
                        max-height: 90vh;
                        width: auto;
                        height: auto;
                        display: block;
                    `;

                    hoverPreview.appendChild(largeImg);
                    document.body.appendChild(hoverPreview);

                    // Add hover events to show/hide the preview
                    smallImg.addEventListener('mouseenter', () => {
                        hoverPreview.style.display = 'block';
                    });

                    smallImg.addEventListener('mouseleave', () => {
                        hoverPreview.style.display = 'none';
                    });

                    // Also show on item hover
                    item.addEventListener('mouseenter', () => {
                        hoverPreview.style.display = 'block';
                    });

                    item.addEventListener('mouseleave', () => {
                        hoverPreview.style.display = 'none';
                    });

                    // Cleanup when dropdown is removed or item is removed
                    const cleanup = () => {
                        if (hoverPreview.parentNode) {
                            hoverPreview.remove();
                        }
                    };

                    // Store cleanup function for later
                    item._cleanupPreview = cleanup;

                    // Create text span
                    const textSpan = document.createElement('span');
                    textSpan.textContent = text;

                    wrapper.appendChild(smallImg);
                    wrapper.appendChild(textSpan);

                    item.textContent = '';
                    item.appendChild(wrapper);
                    //console.log('Image injected successfully for:', text);
                } else {
                    //console.log('No image path found for:', text);
                }
            });
        });

        observer.observe(container, {
            childList: true,
            subtree: true
        });
    }

    return {
        init: initPathAutocompleteImageInjector,
        update: updateImageMap
    };
})();
