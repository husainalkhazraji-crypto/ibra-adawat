import SwiftUI

/// "My apps": adverts for Ibrahim's other apps, read from promo/apps.json in the repository and kept for offline use.
struct PromoApp: Identifiable, Hashable {
    let name: String, ar: String, en: String, link: String, color: String?, badge: String?, icon: String?
    var id: String { name }
    var text: String { L.isArabic ? ar : en }

    var tint: Color {
        guard let hex = color?.trimmingCharacters(in: CharacterSet(charactersIn: "#")), hex.count == 6, let value = Int(hex, radix: 16) else { return Style.brandRed }
        return Color(red: Double((value >> 16) & 0xFF) / 255, green: Double((value >> 8) & 0xFF) / 255, blue: Double(value & 0xFF) / 255)
    }

    var initials: String {
        let letters = name.replacingOccurrences(of: "IBRA", with: "").replacingOccurrences(of: "iBRA", with: "").filter { $0.isLetter || $0.isNumber }
        return letters.isEmpty ? "IB" : String(letters.prefix(2)).uppercased()
    }
}

@MainActor
final class PromoFeed: ObservableObject {
    @Published var apps: [PromoApp] = []
    private let url = URL(string: "https://raw.githubusercontent.com/ibrata2/ibra-adawat/main/promo/apps.json")!

    init() { apps = Self.parse(UserDefaults.standard.data(forKey: "promo") ?? Data()) }

    static func parse(_ data: Data) -> [PromoApp] {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let list = root["apps"] as? [[String: Any]] else { return [] }
        return list.compactMap { app in
            guard let name = app["name"] as? String, !name.isEmpty else { return nil }
            let ar = app["ar"] as? String ?? app["en"] as? String ?? ""
            return PromoApp(name: name, ar: ar, en: app["en"] as? String ?? ar, link: app["link"] as? String ?? "https://ibraprogram.netlify.app/",
                            color: app["color"] as? String, badge: app["badge"] as? String, icon: app["icon"] as? String)
        }
    }

    func refresh() async {
        var request = URLRequest(url: url)
        request.cachePolicy = .reloadIgnoringLocalCacheData
        guard let (data, _) = try? await URLSession.shared.data(for: request) else { return }
        let parsed = Self.parse(data)
        guard !parsed.isEmpty else { return }
        UserDefaults.standard.set(data, forKey: "promo")
        apps = parsed
    }
}

struct MyAppsCard: View {
    @StateObject private var feed = PromoFeed()
    @Environment(\.openURL) private var openURL

    var body: some View {
        Group {
            if !feed.apps.isEmpty {
                VStack(alignment: .leading, spacing: 10) {
                    Text(L.t("My apps", "تطبيقاتي")).font(.headline)
                    ForEach(feed.apps) { app in
                        Button { if let url = URL(string: app.link) { openURL(url) } } label: {
                            HStack(spacing: 12) {
                                ZStack {
                                    RoundedRectangle(cornerRadius: 11, style: .continuous).fill(app.tint)
                                    Text(app.initials).font(.headline.bold()).foregroundStyle(.white)
                                    if let icon = app.icon, let url = URL(string: icon) {
                                        AsyncImage(url: url) { image in image.resizable().scaledToFill() } placeholder: { Color.clear }
                                            .clipShape(RoundedRectangle(cornerRadius: 11, style: .continuous))
                                    }
                                }
                                .frame(width: 46, height: 46)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(app.badge.map { "\(app.name) · \($0)" } ?? app.name).font(.subheadline.bold())
                                    Text(app.text).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                                }
                                Spacer()
                                Image(systemName: "chevron.forward").font(.caption).foregroundStyle(.tertiary)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(14).glass()
            }
        }
        .task { await feed.refresh() }
    }
}
