local Load = select(2, ...)
local DataToColor = unpack(Load)

if not DataToColor.DATA_CONFIG.AUTO_SELL_GREY_ITEMS then
    return
end

local CreateFrame = CreateFrame
local UIParent = UIParent
local C_Timer = C_Timer

local MerchantFrame = MerchantFrame
local UseContainerItem = DataToColor.UseContainerItem

local GetContainerItemInfo = DataToColor.GetContainerItemInfo
local GetContainerNumSlots = DataToColor.GetContainerNumSlots
local GetContainerItemLink = DataToColor.GetContainerItemLink
local GetItemInfo = GetItemInfo
local GetCoinTextureString = GetCoinTextureString

local MERCHANT_SELLING = 9999997
local MERCHANT_SELLING_FINISHED = 9999996

local mFrame = CreateFrame("Frame", nil, UIParent)
mFrame:RegisterEvent("MERCHANT_SHOW")
mFrame:RegisterEvent("MERCHANT_CLOSED")

local mIterationCount, mIterationInterval, mTotalPrice = 500, 0.2, 0
local mSellJunkTicker, mBagID, mBagSlot
local mSelling = false

mFrame:SetScript("OnEvent", function(self, event)
    if event == "MERCHANT_SHOW" then
        mTotalPrice, mBagID, mBagSlot = 0, -1, -1

        if mSellJunkTicker then mSellJunkTicker:Cancel() end
        mSellJunkTicker = C_Timer.NewTicker(mIterationInterval, SellJunkFunc, mIterationCount)
        mFrame:RegisterEvent("ITEM_LOCKED")
        mFrame:RegisterEvent("ITEM_UNLOCKED")
    elseif event == "ITEM_LOCKED" then
        mFrame:UnregisterEvent("ITEM_LOCKED")
    elseif event == "ITEM_UNLOCKED" then
        mFrame:UnregisterEvent("ITEM_UNLOCKED")
        if mBagID and mBagSlot and mBagID ~= -1 and mBagSlot ~= -1 then
            local texture, count, locked = GetContainerItemInfo(mBagID, mBagSlot)
            if count and not locked then
                StopSelling()
            end
        end
    elseif event == "MERCHANT_CLOSED" then
        StopSelling()
    end
end)

function StopSelling()
    if mSellJunkTicker then mSellJunkTicker:Cancel() end
    mFrame:UnregisterEvent("ITEM_LOCKED")
    mFrame:UnregisterEvent("ITEM_UNLOCKED")

    if mSelling then
        mSelling = false
        DataToColor.gossipQueue:push(MERCHANT_SELLING_FINISHED)
    end
end

function SellJunkFunc()
    local soldCount, rarity, itemPrice = 0, 0, 0
    local itemLink

    for bagID = 0, 4 do
        for bagSlot = 1, GetContainerNumSlots(bagID) do
            itemLink = GetContainerItemLink(bagID, bagSlot)
            if itemLink then
                _, _, rarity, _, _, _, _, _, _, _, itemPrice = GetItemInfo(itemLink)
                local _, itemCount = GetContainerItemInfo(bagID, bagSlot)
                if rarity == 0 and itemPrice ~= 0 then
                    if not MerchantFrame:IsShown() then
                        -- If merchant frame is not open, stop selling
                        DataToColor:Print("Unable to sell. Merchant Frame is closed!")
                        StopSelling()
                        return
                    end

                    soldCount = soldCount + 1
                    UseContainerItem(bagID, bagSlot)

                    if not mSelling then
                        mSelling = true
                        DataToColor.gossipQueue:push(MERCHANT_SELLING)
                    end

                    if mSellJunkTicker._remainingIterations == mIterationCount then
                        mTotalPrice = mTotalPrice + (itemPrice * itemCount)
                        -- Store first sold bag slot for analysis
                        if soldCount == 1 then
                            mBagID, mBagSlot = bagID, bagSlot
                            if not mSelling then
                                mSelling = true
                                DataToColor.gossipQueue:push(MERCHANT_SELLING)
                            end
                        end
                    end
                end
            end
        end
    end

    -- Stop selling if no items were sold for this iteration or iteration limit was reached
    if soldCount == 0 or mSellJunkTicker and mSellJunkTicker._remainingIterations == 1 then
        StopSelling()
        if mTotalPrice > 0 then
            DataToColor:Print("Total Price for all items: " .. GetCoinTextureString(mTotalPrice))
        end
    end
end
