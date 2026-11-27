import SwiftUI

struct SettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @AppStorage("style") private var styleId = Style.glassRedWhite.rawValue
    @AppStorage("language") private var language = "system"

    var body: some View {
        NavigationStack {
            Form {
                Section(L.t("Style", "الستايل")) {
                    Picker(L.t("Style", "الستايل"), selection: $styleId) {
                        ForEach(Style.allCases) { Text($0.title).tag($0.rawValue) }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
                Section(L.t("Language", "اللغة")) {
                    Picker(L.t("Language", "اللغة"), selection: $language) {
                        Text(L.t("Phone language", "لغة الجهاز")).tag("system")
                        Text("العربية").tag("ar")
                        Text("English").tag("en")
                    }
                    .pickerStyle(.segmented)
                }
                Section(L.t("Results", "النتائج")) {
                    Text(L.t("Results are saved in Files › On My iPhone › nice tools, and you can share them or save them to Photos from each task.",
                             "النتائج تنحفظ بتطبيق الملفات ← على الآيفون ← nice tools، وتگدر تشاركها أو تحفظها بالصور من كل مهمة."))
                        .font(.footnote).foregroundStyle(.secondary)
                }
                Section(L.t("About", "حول")) {
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            Image("Logo").resizable().frame(width: 44, height: 44)
                            VStack(alignment: .leading) {
                                Text("nice tools").font(.subheadline.bold())
                                Text(L.t("Version", "النسخة") + " " + (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "")).font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        Text("برمجة وتطوير Husain alkhazraji").font(.headline)
                    }
                    Text("Based on IBMC by Ibrahim Rosem and Adawat by Motalim · GPL v3").font(.caption).foregroundStyle(.secondary)
                }
            }
            .navigationTitle(L.t("Settings", "الإعدادات"))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button(L.t("Done", "تم")) { dismiss() } } }
        }
    }
}
