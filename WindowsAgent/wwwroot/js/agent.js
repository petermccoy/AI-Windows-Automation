// Mic recorder using the Web Audio API instead of MediaRecorder.
// MediaRecorder ignores the requested mime type on most browsers and
// records WebM/Opus regardless of what you ask for — feeding that to
// Azure Speech (which wants raw 16kHz/16-bit/mono PCM) silently fails.
// This captures real PCM samples, downsamples to 16kHz, and wraps them
// in a canonical 44-byte WAV header so the bytes are what they claim to be.

window.micRecorder = (function () {
    let audioContext, processor, source, stream;
    let chunks = [];
    const TARGET_SAMPLE_RATE = 16000;

    return {
        start: async function () {
            chunks = [];
            stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            audioContext = new (window.AudioContext || window.webkitAudioContext)();
            source = audioContext.createMediaStreamSource(stream);

            // ScriptProcessorNode is deprecated but universally supported;
            // an AudioWorklet is the modern replacement if you want to remove
            // the deprecation warning later.
            processor = audioContext.createScriptProcessor(4096, 1, 1);
            processor.onaudioprocess = e => chunks.push(new Float32Array(e.inputBuffer.getChannelData(0)));

            source.connect(processor);
            processor.connect(audioContext.destination);
        },

        stop: function () {
            return new Promise(resolve => {
                processor.disconnect();
                source.disconnect();
                stream.getTracks().forEach(t => t.stop());

                const nativeRate = audioContext.sampleRate;
                const merged = mergeFloat32(chunks);
                const downsampled = downsample(merged, nativeRate, TARGET_SAMPLE_RATE);
                const pcm16 = floatTo16BitPCM(downsampled);
                const wavBuffer = encodeWav(pcm16, TARGET_SAMPLE_RATE);

                audioContext.close();
                resolve(arrayBufferToBase64(wavBuffer));
            });
        }
    };

    function mergeFloat32(parts) {
        const length = parts.reduce((sum, p) => sum + p.length, 0);
        const result = new Float32Array(length);
        let offset = 0;
        for (const p of parts) { result.set(p, offset); offset += p.length; }
        return result;
    }

    function downsample(buffer, fromRate, toRate) {
        if (toRate === fromRate) return buffer;
        const ratio = fromRate / toRate;
        const newLength = Math.round(buffer.length / ratio);
        const result = new Float32Array(newLength);
        for (let i = 0; i < newLength; i++) result[i] = buffer[Math.floor(i * ratio)];
        return result;
    }

    function floatTo16BitPCM(floatSamples) {
        const out = new Int16Array(floatSamples.length);
        for (let i = 0; i < floatSamples.length; i++) {
            const s = Math.max(-1, Math.min(1, floatSamples[i]));
            out[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
        }
        return out;
    }

    function encodeWav(pcm16, sampleRate) {
        const buffer = new ArrayBuffer(44 + pcm16.length * 2);
        const view = new DataView(buffer);

        writeStr(view, 0, "RIFF");
        view.setUint32(4, 36 + pcm16.length * 2, true);
        writeStr(view, 8, "WAVE");
        writeStr(view, 12, "fmt ");
        view.setUint32(16, 16, true);       // fmt chunk size
        view.setUint16(20, 1, true);        // PCM
        view.setUint16(22, 1, true);        // mono
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true); // byte rate
        view.setUint16(32, 2, true);        // block align
        view.setUint16(34, 16, true);       // bits per sample
        writeStr(view, 36, "data");
        view.setUint32(40, pcm16.length * 2, true);

        let offset = 44;
        for (let i = 0; i < pcm16.length; i++, offset += 2) view.setInt16(offset, pcm16[i], true);

        return buffer;
    }

    function writeStr(view, offset, str) {
        for (let i = 0; i < str.length; i++) view.setUint8(offset + i, str.charCodeAt(i));
    }

    function arrayBufferToBase64(buffer) {
        let binary = "";
        const bytes = new Uint8Array(buffer);
        const chunkSize = 0x8000;
        for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
        }
        return btoa(binary);
    }
})();

window.ttsPlayer = {
    play: function (base64Mp3) {
        const audio = document.getElementById("ttsPlayer");
        audio.src = "data:audio/mpeg;base64," + base64Mp3;
        audio.style.display = "block";
        audio.play();
    }
};
