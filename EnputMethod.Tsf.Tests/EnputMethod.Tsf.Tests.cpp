#include "../EnputMethod.Tsf/CandidateSelection.h"

#include <cstdlib>
#include <iostream>

namespace {

void Expect(bool condition, const char* message) {
    if (condition) return;
    std::cerr << "FAILED: " << message << '\n';
    std::exit(1);
}

void ExpectIndex(WPARAM key, int expected, const char* message) {
    Expect(enput::CandidateIndex(key) == expected, message);
}

void VerifyDigitMappings() {
    for (int index = 0; index < 9; ++index) {
        ExpectIndex(static_cast<WPARAM>('1' + index), index, "Top-row digit did not map to its candidate index.");
        ExpectIndex(static_cast<WPARAM>(VK_NUMPAD1 + index), index, "Numpad digit did not map to its candidate index.");
    }
    ExpectIndex('0', -1, "Zero must not select a candidate.");
    ExpectIndex('A', -1, "Letters must not select a candidate.");
    ExpectIndex(VK_F1, -1, "Function keys must not select a candidate.");
}

void VerifyCandidateBounds() {
    for (std::size_t candidateCount = 1; candidateCount <= 9; ++candidateCount) {
        std::size_t index = 99;
        Expect(enput::TryGetCandidateIndex(static_cast<WPARAM>('1' + candidateCount - 1), candidateCount, &index), "Last visible candidate must be selectable.");
        Expect(index == candidateCount - 1, "Selected candidate index did not match the visible candidate.");
        if (candidateCount < 9) {
            Expect(!enput::TryGetCandidateIndex(static_cast<WPARAM>('1' + candidateCount), candidateCount, &index), "A digit beyond the visible candidate count must pass through.");
        }
    }
    Expect(!enput::TryGetCandidateIndex('1', 0, nullptr), "No digit can select when no candidates are visible.");
}

}

int main() {
    VerifyDigitMappings();
    VerifyCandidateBounds();
    std::cout << "TSF candidate selection tests passed.\n";
    return 0;
}
