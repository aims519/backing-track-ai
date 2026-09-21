import SwiftUI

extension JobDisposition {
    var historyLabel: String {
        switch self {
            case .ready:
                "Ready"
            case .inFlight:
                "In progress"
            case .serverFailed:
                "Failed"
            case .timedOut:
                "Timed out"
            case .lost:
                "May have finished"
            case .pollError:
                "Couldn't confirm"
            case .cancelled:
                "Cancelled"
        }
    }
    
    var historyTint: Color {
        switch self {
            case .ready:
                .green
            case .inFlight:
                .secondary
            case .serverFailed:
                .red
            case .timedOut:
                .orange
            case .lost:
                .secondary
            case .pollError:
                .orange
            case .cancelled:
                .secondary
        }
    }

    var historyIcon: String {
        switch self {
            case .ready:
                "checkmark.circle.fill"
            case .inFlight:
                "arrow.triangle.2.circlepath"
            case .serverFailed:
                "exclamationmark.triangle.fill"
            case .timedOut:
                "clock.badge.exclamationmark"
            case .lost:
                "questionmark.circle"
            case .pollError:
                "wifi.exclamationmark"
            case .cancelled:
                "xmark.circle"
        }
    }
}

