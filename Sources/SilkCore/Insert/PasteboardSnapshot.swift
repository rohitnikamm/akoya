import AppKit

/// A byte-for-byte copy of everything on a pasteboard: every type of every item.
public struct PasteboardSnapshot {
    private let items: [[NSPasteboard.PasteboardType: Data]]

    public init(capturing pasteboard: NSPasteboard) {
        items = (pasteboard.pasteboardItems ?? []).map { item in
            var dataByType: [NSPasteboard.PasteboardType: Data] = [:]
            for type in item.types {
                if let data = item.data(forType: type) { dataByType[type] = data }
            }
            return dataByType
        }
    }

    /// Replaces the pasteboard's contents with the snapshot. An empty snapshot leaves it empty.
    public func restore(to pasteboard: NSPasteboard) {
        pasteboard.clearContents()
        let rebuilt = items.filter { !$0.isEmpty }.map { dataByType -> NSPasteboardItem in
            let item = NSPasteboardItem()
            for (type, data) in dataByType { item.setData(data, forType: type) }
            return item
        }
        if !rebuilt.isEmpty { pasteboard.writeObjects(rebuilt) }
    }
}
