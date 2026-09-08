enum PlaybackState: Equatable {
    case idle
    case playing
    case paused
    case failed(String)
}
