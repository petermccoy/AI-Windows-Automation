// Minimal mic recorder using MediaRecorder. Records to webm/opus in the
// browser; for a production build, transcode to 16kHz mono WAV either here
// (via an AudioContext) or server-side before handing to Azure Speech.
// This stub keeps the wiring visible without pulling in a transcode library.

window.micRecorder = (function () {
    let mediaRecorder;
    let chunks = [];

    return {
        start: async function () {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            chunks = [];
            mediaRecorder = new MediaRecorder(stream);
            mediaRecorder.ondataavailable = e => chunks.push(e.data);
            mediaRecorder.start();
        },
        stop: function () {
            return new Promise(resolve => {
                mediaRecorder.onstop = async () => {
                    const blob = new Blob(chunks, { type: "audio/wav" });
                    const buffer = await blob.arrayBuffer();
                    const base64 = btoa(String.fromCharCode(...new Uint8Array(buffer)));
                    resolve(base64);
                };
                mediaRecorder.stop();
            });
        }
    };
})();

window.ttsPlayer = {
    play: function (base64Mp3) {
        const audio = document.getElementById("ttsPlayer");
        audio.src = "data:audio/mpeg;base64," + base64Mp3;
        audio.style.display = "block";
        audio.play();
    }
};
