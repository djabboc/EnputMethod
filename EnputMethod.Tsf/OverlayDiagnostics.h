#pragma once

#include <string_view>

namespace enput {

// Best-effort, cross-process diagnostic trace for the TSF-to-WPF handoff.
void WriteOverlayDiagnostic(std::string_view event, std::string_view detail = {});

} // namespace enput
