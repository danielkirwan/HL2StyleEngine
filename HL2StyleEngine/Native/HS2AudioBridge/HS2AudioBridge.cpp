#include <atomic>
#include <algorithm>
#include <chrono>
#include <cstring>
#include <memory>
#include <vector>
#include "soloud.h"
#include "soloud_audiosource.h"
#define MINIAUDIO_IMPLEMENTATION
#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_NULL
#define MA_ENABLE_ONLY_SPECIFIC_BACKENDS
#define MA_ENABLE_WASAPI
#include "miniaudio.h"

#define API extern "C" __declspec(dllexport)
using Clock = std::chrono::steady_clock;

struct PcmSource;
struct PcmInstance final : SoLoud::AudioSourceInstance {
    PcmSource& source;
    uint64_t cursor = 0;
    bool ended = false;
    explicit PcmInstance(PcmSource& value) : source(value) {}
    unsigned getAudio(float*, unsigned, unsigned) override;
    bool hasEnded() override { return ended; }
};

// SPSC ring: only the C# control worker produces; only the native mixer consumes.
struct PcmSource final : SoLoud::AudioSource {
    std::vector<float> samples;
    bool streaming;
    std::atomic<uint64_t> read{0}, write{0}, underruns{0};
    std::atomic<bool> eof{false};
    PcmSource(unsigned rate, unsigned channels, unsigned frames, bool stream)
        : samples(static_cast<size_t>(frames) * channels), streaming(stream) {
        mBaseSamplerate = static_cast<float>(rate); mChannels = channels;
    }
    SoLoud::AudioSourceInstance* createInstance() override { return new PcmInstance(*this); }
};

unsigned PcmInstance::getAudio(float* out, unsigned frames, unsigned stride) {
    const auto channels = source.mChannels;
    const uint64_t capacity = source.samples.size() / channels;
    auto start = source.streaming ? source.read.load(std::memory_order_relaxed) : cursor;
    auto limit = source.streaming ? source.write.load(std::memory_order_acquire) : capacity;
    unsigned done = 0;
    for (; done < frames; ++done) {
        if (start == limit) {
            if (!source.streaming && (mFlags & LOOPING)) { start = 0; ++mLoopCount; }
            else break;
        }
        for (unsigned c = 0; c < channels; ++c)
            out[c * stride + done] = source.samples[(start % capacity) * channels + c];
        ++start;
    }
    if (source.streaming) {
        source.read.store(start, std::memory_order_release);
        ended = done < frames && source.eof.load(std::memory_order_acquire);
        if (done < frames && !ended) {
            source.underruns.fetch_add(1, std::memory_order_relaxed);
            for (unsigned c = 0; c < channels; ++c)
                std::fill(out + c * stride + done, out + c * stride + frames, 0.f);
            return frames;
        }
    } else { cursor = start; ended = done < frames; }
    return done;
}

struct Audio {
    SoLoud::Soloud mixer;
    ma_device device{};
    bool initialized = false;
    std::atomic<uint64_t> callbacks{0}, maxNs{0}, overruns{0};
    void render(float* out, unsigned frames) {
        // SoLoud's scratch capacity is fixed at initialization. Device callbacks
        // and offline requests may be larger; never forward an unbounded block.
        for (unsigned offset = 0; offset < frames; offset += 512)
            mixer.mix(out + size_t(offset) * 2, std::min(512u, frames - offset));
    }
    static void mix(ma_device* device, void* out, const void*, ma_uint32 frames) {
        auto& self = *static_cast<Audio*>(device->pUserData);
        const auto begin = Clock::now();
        self.render(static_cast<float*>(out), frames);
        auto ns = static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(Clock::now() - begin).count());
        self.callbacks.fetch_add(1, std::memory_order_relaxed);
        auto previous = self.maxNs.load(std::memory_order_relaxed);
        while (previous < ns && !self.maxNs.compare_exchange_weak(previous, ns, std::memory_order_relaxed)) {}
        if (ns > uint64_t(frames) * 1000000000 / 48000)
            self.overruns.fetch_add(1, std::memory_order_relaxed);
    }
    bool open() {
        if (initialized) { ma_device_uninit(&device); initialized = false; }
        auto config = ma_device_config_init(ma_device_type_playback);
        config.playback.format = ma_format_f32;
        config.playback.channels = 2;
        config.sampleRate = 48000;
        config.periodSizeInFrames = 512;
        config.dataCallback = mix;
        config.pUserData = this;
        if (ma_device_init(nullptr, &config, &device) != MA_SUCCESS) return false;
        initialized = true;
        if (ma_device_start(&device) != MA_SUCCESS) {
            ma_device_uninit(&device); initialized = false; return false;
        }
        return true;
    }
    ~Audio() { if (initialized) ma_device_uninit(&device); mixer.deinit(); }
};

API Audio* hs2_audio_create(int output) {
    try {
        auto value = std::make_unique<Audio>();
        if (value->mixer.init(SoLoud::Soloud::CLIP_ROUNDOFF | SoLoud::Soloud::LEFT_HANDED_3D,
            SoLoud::Soloud::NULLDRIVER, 48000, 512, 2)) return nullptr;
        value->mixer.setMaxActiveVoiceCount(32);
        value->mixer.setGlobalVolume(.7f);
        if (output) value->open();
        return value.release();
    } catch (...) { return nullptr; }
}
API void hs2_audio_destroy(Audio* a) { delete a; }
API int hs2_audio_output(Audio* a, int enabled) {
    if (enabled) return a->open() ? 1 : 0;
    if (a->initialized) ma_device_uninit(&a->device);
    a->initialized = false; return 0;
}
API int hs2_audio_ready(Audio* a) { return a->initialized && ma_device_is_started(&a->device); }
API PcmSource* hs2_audio_source(unsigned rate, unsigned channels, unsigned frames, int stream, const float* pcm) {
    if ((channels != 1 && channels != 2) || !frames || frames > 48000 * 30 || rate < 8000 || rate > 96000) return nullptr;
    try {
        auto source = std::make_unique<PcmSource>(rate, channels, frames, stream != 0);
        if (!stream && pcm) std::copy(pcm, pcm + size_t(frames) * channels, source->samples.begin());
        source->setInaudibleBehavior(true, false);
        source->set3dDopplerFactor(0);
        return source.release();
    } catch (...) { return nullptr; }
}
API void hs2_audio_free(PcmSource* source) { source->stop(); delete source; }
API unsigned hs2_audio_write(PcmSource* source, const float* pcm, unsigned frames, int eof) {
    auto write = source->write.load(std::memory_order_relaxed);
    auto read = source->read.load(std::memory_order_acquire);
    const auto capacity = source->samples.size() / source->mChannels;
    frames = std::min(frames, static_cast<unsigned>(capacity - (write - read)));
    for (unsigned i = 0; i < frames; ++i)
        for (unsigned c = 0; c < source->mChannels; ++c)
            source->samples[((write + i) % capacity) * source->mChannels + c] = pcm[i * source->mChannels + c];
    source->write.store(write + frames, std::memory_order_release);
    if (eof) source->eof.store(true, std::memory_order_release);
    return frames;
}
API unsigned hs2_audio_space(PcmSource* s) { return static_cast<unsigned>(s->samples.size() / s->mChannels - (s->write.load() - s->read.load())); }
API uint64_t hs2_audio_starved(PcmSource* s) { return s->underruns.load(); }
API unsigned hs2_audio_play(Audio* a, PcmSource* s, int spatial, int loop, float x, float y, float z, float gain, float pitch, float nearDistance, float farDistance) {
    try {
        s->setLooping(loop != 0 && !s->streaming);
        s->set3dMinMaxDistance(nearDistance, farDistance);
        s->set3dAttenuation(SoLoud::AudioSource::LINEAR_DISTANCE, 1.f);
        auto voice = spatial ? a->mixer.play3d(*s, x, y, z, 0, 0, 0, gain, true) : a->mixer.play(*s, gain, 0, true);
        a->mixer.setRelativePlaySpeed(voice, pitch);
        a->mixer.update3dAudio();
        return voice;
    } catch (...) { return 0; }
}
API void hs2_audio_stop(Audio* a, unsigned v) { a->mixer.stop(v); }
API int hs2_audio_valid(Audio* a, unsigned v) { return a->mixer.isValidVoiceHandle(v); }
API void hs2_audio_voice(Audio* a, unsigned v, float x, float y, float z, float gain, int paused) {
    a->mixer.set3dSourcePosition(v, x, y, z); a->mixer.setVolume(v, gain); a->mixer.setPause(v, paused != 0);
}
API void hs2_audio_listener(Audio* a, float x, float y, float z, float fx, float fy, float fz, float ux, float uy, float uz) {
    a->mixer.set3dListenerParameters(x, y, z, fx, fy, fz, ux, uy, uz); a->mixer.update3dAudio();
}
API void hs2_audio_stats(Audio* a, uint64_t* out) {
    out[0] = a->callbacks.load(); out[1] = a->maxNs.load(); out[2] = a->overruns.load();
    out[3] = a->mixer.getActiveVoiceCount(); out[4] = a->mixer.getVoiceCount();
    out[5] = a->initialized ? a->device.playback.internalPeriodSizeInFrames : 0;
}
// Offline validation only: never mix twice while a live device owns the mixer.
API int hs2_audio_render(Audio* a, float* pcm, unsigned frames) {
    if (a->initialized || frames > 48000) return 0;
    a->render(pcm, frames); return 1;
}
