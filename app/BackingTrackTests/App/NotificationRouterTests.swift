import Foundation
import Testing
import UserNotifications
@testable import BackingTrack

@MainActor
struct NotificationRouterTests {

    // MARK: - Payload parsing

    @Test func jobIDReadsTheApisTopLevelJobIdKey() {
        let id = UUID()
        let userInfo: [AnyHashable: Any] = [
            "aps": ["alert": ["title": "Track Ready", "body": "C Ionian - 120 BPM - rock"]],
            "jobId": id.uuidString,
        ]

        #expect(NotificationRouter.jobID(from: userInfo) == id)
    }

    @Test func jobIDIsNilWithoutAJobId() {
        #expect(NotificationRouter.jobID(from: ["aps": ["alert": "hello"]]) == nil)
    }

    @Test func jobIDIsNilForAMalformedJobId() {
        #expect(NotificationRouter.jobID(from: ["jobId": "not-a-uuid"]) == nil)
        #expect(NotificationRouter.jobID(from: ["jobId": 42]) == nil)
    }

    // MARK: - Foreground presentation

    @Test func showsTheBannerWhenNoProgressScreenIsUp() {
        let router = NotificationRouter()

        #expect(router.presentationOptions(forJobID: UUID()) == [.banner, .list, .sound])
    }

    @Test func suppressesThePushForTheJobBeingWatched() {
        let router = NotificationRouter()
        let id = UUID()
        router.watchedJobID = id

        #expect(router.presentationOptions(forJobID: id) == [])
    }

    @Test func stillShowsAPushForADifferentJobWhileWatchingAnother() {
        let router = NotificationRouter()
        router.watchedJobID = UUID()

        #expect(router.presentationOptions(forJobID: UUID()) == [.banner, .list, .sound])
    }

    @Test func showsAPushWithNoJobIdEvenWhileWatching() {
        // e.g. a hand-sent test push — nothing to match against, so don't swallow it.
        let router = NotificationRouter()
        router.watchedJobID = UUID()

        #expect(router.presentationOptions(forJobID: nil) == [.banner, .list, .sound])
    }
}
