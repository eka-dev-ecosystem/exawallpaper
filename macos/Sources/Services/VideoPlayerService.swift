import AppKit
import AVFoundation

public final class VideoPlayerService {
    private var queuePlayer: AVQueuePlayer?
    private var playerLooper: AVPlayerLooper?
    private var managedLayers: [AVPlayerLayer] = []
    private var currentVideoGravity: AVLayerVideoGravity = .resizeAspectFill

    public let playerLayer = AVPlayerLayer()

    public var isPlaying: Bool {
        return queuePlayer?.timeControlStatus == .playing
    }

    public init() {
        playerLayer.videoGravity = currentVideoGravity
        playerLayer.drawsAsynchronously = true
        managedLayers.append(playerLayer)
    }

    deinit {
        dispose()
    }

    public func makeLayer() -> AVPlayerLayer {
        let layer = AVPlayerLayer(player: queuePlayer)
        layer.videoGravity = currentVideoGravity
        layer.drawsAsynchronously = true
        managedLayers.append(layer)
        return layer
    }

    public func loadVideo(url: URL, isMuted: Bool, volume: Float, scalingMode: String) {
        disposePlayerOnly()

        let asset = AVURLAsset(url: url)
        let playerItem = AVPlayerItem(asset: asset)

        let player = AVQueuePlayer()
        player.preventsDisplaySleepDuringVideoPlayback = false
        player.automaticallyWaitsToMinimizeStalling = false
        player.actionAtItemEnd = .none

        self.playerLooper = AVPlayerLooper(player: player, templateItem: playerItem)
        self.queuePlayer = player
        for layer in managedLayers {
            layer.player = player
        }

        setScalingMode(scalingMode)
        setMuted(isMuted)
        setVolume(volume)

        player.play()
    }

    public func dispose() {
        disposePlayerOnly()
        managedLayers.removeAll()
    }

    private func disposePlayerOnly() {
        playerLooper?.disableLooping()
        playerLooper = nil
        queuePlayer?.pause()
        queuePlayer?.removeAllItems()
        queuePlayer = nil
        for layer in managedLayers {
            layer.player = nil
        }
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
        let gravity: AVLayerVideoGravity
        switch mode {
        case "AspectFit":
            gravity = .resizeAspect
        case "Stretch":
            gravity = .resize
        default:
            gravity = .resizeAspectFill
        }
        currentVideoGravity = gravity
        for layer in managedLayers {
            layer.videoGravity = gravity
        }
    }
}
