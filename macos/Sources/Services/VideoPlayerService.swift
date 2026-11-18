import AppKit
import AVFoundation

public final class VideoPlayerService {
    private var queuePlayer: AVQueuePlayer?
    private var playerLooper: AVPlayerLooper?
    public let playerLayer = AVPlayerLayer()

    public var isPlaying: Bool {
        return queuePlayer?.timeControlStatus == .playing
    }

    public init() {
        playerLayer.videoGravity = .resizeAspectFill
        playerLayer.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        playerLayer.drawsAsynchronously = true
    }

    public func loadVideo(url: URL, isMuted: Bool, volume: Float, scalingMode: String) {
        playerLooper?.disableLooping()
        playerLooper = nil
        queuePlayer?.pause()
        queuePlayer?.removeAllItems()

        let asset = AVURLAsset(url: url)
        let playerItem = AVPlayerItem(asset: asset)

        let player = AVQueuePlayer()
        player.preventsDisplaySleepDuringVideoPlayback = false
        player.automaticallyWaitsToMinimizeStalling = false
        player.actionAtItemEnd = .none

        self.playerLooper = AVPlayerLooper(player: player, templateItem: playerItem)

        self.queuePlayer = player
        self.playerLayer.player = player

        setScalingMode(scalingMode)
        setMuted(isMuted)
        setVolume(volume)

        player.play()
    }

    public func play() {
        queuePlayer?.play()
    }

    public func pause() {
        queuePlayer?.pause()
    }

    public func setMuted(_ isMuted: Bool) {
        queuePlayer?.isMuted = isMuted
    }

    public func setVolume(_ volume: Float) {
        queuePlayer?.volume = max(0.0, min(1.0, volume))
    }

    public func setScalingMode(_ mode: String) {
        switch mode {
        case "AspectFit":
            playerLayer.videoGravity = .resizeAspect
        case "Stretch":
            playerLayer.videoGravity = .resize
        default:
            playerLayer.videoGravity = .resizeAspectFill
        }
    }
}
