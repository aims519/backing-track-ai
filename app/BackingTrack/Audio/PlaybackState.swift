enum PlaybackState: Equatable {
    case idle
    case countingIn
    case playing
    case paused
    case failed(String)
}
