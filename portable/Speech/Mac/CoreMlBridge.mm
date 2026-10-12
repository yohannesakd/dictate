// Core ML encoder ABI from whisper.cpp, MIT: https://github.com/ggml-org/whisper.cpp
#import <Foundation/Foundation.h>
#import <CoreML/CoreML.h>
#include <atomic>
#include <cstring>

struct whisper_coreml_context {
    const void * model;
};

// The checksum-verified English base encoder produces 1500 frames with 512 features.
static constexpr NSUInteger encoder_output_count = 1500 * 512;

static std::atomic<bool> encoder_enabled { true };
static std::atomic<bool> prediction_failed { false };
static std::atomic<bool> neural_configuration { false };

extern "C" int dictate_coreml_available() {
    @autoreleasepool {
        for (id<MLComputeDeviceProtocol> device in MLModel.availableComputeDevices)
            if ([device isKindOfClass:[MLNeuralEngineComputeDevice class]]) return 1;
        return 0;
    }
}

extern "C" void dictate_coreml_set_enabled(int enabled) {
    encoder_enabled = enabled != 0;
    prediction_failed = false;
    neural_configuration = false;
}

extern "C" int dictate_coreml_prediction_failed() { return prediction_failed ? 1 : 0; }
extern "C" int dictate_coreml_neural_configuration() { return neural_configuration ? 1 : 0; }

extern "C" whisper_coreml_context * whisper_coreml_init(const char * path) {
    if (!encoder_enabled) return nullptr;
    @autoreleasepool {
        MLModelConfiguration * configuration = [[MLModelConfiguration alloc] init];
        configuration.computeUnits = MLComputeUnitsCPUAndNeuralEngine;
        NSError * error = nil;
        MLModel * model = [MLModel modelWithContentsOfURL:[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]]
                                           configuration:configuration error:&error];
        if (!model) { NSLog(@"Dictate Core ML encoder could not load: %@", error); return nullptr; }
        neural_configuration = model.configuration.computeUnits == MLComputeUnitsCPUAndNeuralEngine;
        return new whisper_coreml_context { CFBridgingRetain(model) };
    }
}

extern "C" void whisper_coreml_free(whisper_coreml_context * context) {
    CFRelease(context->model);
    delete context;
}

extern "C" void whisper_coreml_encode(const whisper_coreml_context * context, int64_t frames, int64_t mel_count, float * mel, float * output) {
    @autoreleasepool {
        NSError * error = nil;
        MLMultiArray * input = [[MLMultiArray alloc] initWithDataPointer:mel shape:@[@1, @(mel_count), @(frames)]
                                  dataType:MLMultiArrayDataTypeFloat32 strides:@[@(frames * mel_count), @(frames), @1]
                               deallocator:nil error:&error];
        MLDictionaryFeatureProvider * features = input ? [[MLDictionaryFeatureProvider alloc]
            initWithDictionary:@{@"logmel_data": [MLFeatureValue featureValueWithMultiArray:input]} error:&error] : nil;
        id<MLFeatureProvider> result = features ? [(__bridge MLModel *)context->model predictionFromFeatures:features error:&error] : nil;
        MLMultiArray * encoded = [result featureValueForName:@"output"].multiArrayValue;
        if (!encoded || encoded.dataType != MLMultiArrayDataTypeFloat32 || encoded.count != encoder_output_count) {
            prediction_failed = true;
            std::memset(output, 0, encoder_output_count * sizeof(float));
            NSLog(@"Dictate Core ML encoder prediction failed: %@; output type=%ld count=%ld", error, (long)encoded.dataType, (long)encoded.count);
            return;
        }
        std::memcpy(output, encoded.dataPointer, encoded.count * sizeof(float));
    }
}
