
document.addEventListener("DOMContentLoaded", function () {

    const SpeechRecognition =
        window.SpeechRecognition ||
        window.webkitSpeechRecognition;

    const buttons = document.querySelectorAll(
        ".voice-search-btn"
    );

    buttons.forEach(function (button) {

        const input = document.getElementById(
            button.dataset.target
        );

        const status = document.getElementById(
            "voiceSearchStatus"
        );

        if (!input) return;

        // Browser support check
        if (!SpeechRecognition) {
            button.disabled = true;
            button.title = "Voice search is not supported";

            if (status) {
                status.textContent =
                    "Voice search is not supported in this browser.";
            }

            return;
        }

        button.addEventListener("click", function () {

            const recognition = new SpeechRecognition();

          recognition.lang =
    document.getElementById("voiceLanguage")?.value
    || "en-US";

            recognition.interimResults = false;
            recognition.maxAlternatives = 1;

            recognition.onstart = function () {
                button.disabled = true;

                if (status) {
                    status.textContent = "Listening...";
                }
            };

            recognition.onresult = function (event) {

                const spokenText =
                    event.results[0][0].transcript;

                input.value = spokenText;

                if (status) {
                    status.textContent =
                        "Searching for: " + spokenText;
                }

                // Submit same form as manual search
                input.form?.requestSubmit();
            };

            recognition.onerror = function (event) {
                if (status) {
                    status.textContent =
                        "Voice search error: " + event.error;
                }
            };

            recognition.onend = function () {
                button.disabled = false;
            };

            try {
                recognition.start();
            }
            catch (error) {
                button.disabled = false;

                if (status) {
                    status.textContent =
                        "Unable to start voice search.";
                }
            }
        });
    });
});
