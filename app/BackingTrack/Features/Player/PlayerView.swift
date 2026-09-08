import SwiftUI

struct PlayerView: View {
    @Bindable var vm: PlayerViewModel
    
    var body: some View {
        VStack() {
            Button { vm.playPause() } label: {
                Text(vm.playbackState == .playing ? "Pause" : "Play")
            }
            tempoCard
        }
        .padding()
    }
    
    private var tempoCard: some View {
        SectionCard("Tempo") {
            VStack(alignment: .leading, spacing: 4) {
                Text("\(vm.tempoBpm) BPM")
                    .font(.title3)
                    .monospacedDigit()
                
                Slider(
                    value: Binding(
                        get: { Double(vm.tempoBpm) },
                        set: { vm.tempoBpm = Int($0.rounded()) }
                    ),
                    in: 40...240,
                    step: 1
                )
                
                if vm.tempoBpm != vm.originalTempoBpm {
                    Button("Reset to original") { vm.resetTempo() }
                }
            }
            .padding()
        }
    }
}

#Preview {
    PlayerView(vm: PlayerViewModel(playableTrack: PlayableTrack.preview()))
}
