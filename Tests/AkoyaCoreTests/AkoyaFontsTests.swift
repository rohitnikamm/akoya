import SwiftUI
import Testing
@testable import AkoyaCore

@Test func outfitRegistersFromResourceBundle() {
    AkoyaFonts.register()
    #expect(AkoyaFonts.isRegistered)
}

@Test func outfitWeightsMapToVariationAxis() {
    #expect(AkoyaFonts.weightAxisValue(.regular) == 400)
    #expect(AkoyaFonts.weightAxisValue(.medium) == 500)
    #expect(AkoyaFonts.weightAxisValue(.semibold) == 600)
}
