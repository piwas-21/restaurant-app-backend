using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Order_Users_UserId",
                table: "Order");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Order_OrderId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_menus_MenuId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_product_variations_ProductVariationId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayment_Order_OrderId",
                table: "OrderPayment");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusHistory_Order_OrderId",
                table: "OrderStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderStatusHistory",
                table: "OrderStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderItems",
                table: "OrderItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderPayment",
                table: "OrderPayment");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Order",
                table: "Order");

            migrationBuilder.RenameTable(
                name: "OrderPayment",
                newName: "order_payments");

            migrationBuilder.RenameTable(
                name: "Order",
                newName: "orders");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "OrderStatusHistory",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "OrderStatusHistory",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "OrderStatusHistory",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "OrderStatusHistory",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ToStatus",
                table: "OrderStatusHistory",
                newName: "to_status");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "OrderStatusHistory",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "FromStatus",
                table: "OrderStatusHistory",
                newName: "from_status");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "OrderStatusHistory",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "OrderStatusHistory",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ChangedBy",
                table: "OrderStatusHistory",
                newName: "changed_by");

            migrationBuilder.RenameColumn(
                name: "ChangedAt",
                table: "OrderStatusHistory",
                newName: "changed_at");

            migrationBuilder.RenameIndex(
                name: "IX_OrderStatusHistory_OrderId",
                table: "OrderStatusHistory",
                newName: "ix_order_status_histories_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderStatusHistory_ChangedAt",
                table: "OrderStatusHistory",
                newName: "IX_OrderStatusHistory_changed_at");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "OrderItems",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "OrderItems",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "VariationName",
                table: "OrderItems",
                newName: "variation_name");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "OrderItems",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "OrderItems",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "UnitPrice",
                table: "OrderItems",
                newName: "unit_price");

            migrationBuilder.RenameColumn(
                name: "SpecialInstructions",
                table: "OrderItems",
                newName: "special_instructions");

            migrationBuilder.RenameColumn(
                name: "ProductVariationId",
                table: "OrderItems",
                newName: "product_variation_id");

            migrationBuilder.RenameColumn(
                name: "ProductName",
                table: "OrderItems",
                newName: "product_name");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "OrderItems",
                newName: "product_id");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "OrderItems",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "MenuId",
                table: "OrderItems",
                newName: "menu_id");

            migrationBuilder.RenameColumn(
                name: "ItemTotal",
                table: "OrderItems",
                newName: "item_total");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "OrderItems",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "OrderItems",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_ProductVariationId",
                table: "OrderItems",
                newName: "ix_order_items_product_variation_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                newName: "ix_order_items_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                newName: "ix_order_items_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_MenuId",
                table: "OrderItems",
                newName: "ix_order_items_menu_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "order_payments",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "order_payments",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "order_payments",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "order_payments",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "order_payments",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TransactionId",
                table: "order_payments",
                newName: "transaction_id");

            migrationBuilder.RenameColumn(
                name: "RefundedAmount",
                table: "order_payments",
                newName: "refunded_amount");

            migrationBuilder.RenameColumn(
                name: "RefundReason",
                table: "order_payments",
                newName: "refund_reason");

            migrationBuilder.RenameColumn(
                name: "RefundDate",
                table: "order_payments",
                newName: "refund_date");

            migrationBuilder.RenameColumn(
                name: "ReferenceNumber",
                table: "order_payments",
                newName: "reference_number");

            migrationBuilder.RenameColumn(
                name: "PaymentNotes",
                table: "order_payments",
                newName: "payment_notes");

            migrationBuilder.RenameColumn(
                name: "PaymentMethod",
                table: "order_payments",
                newName: "payment_method");

            migrationBuilder.RenameColumn(
                name: "PaymentGateway",
                table: "order_payments",
                newName: "payment_gateway");

            migrationBuilder.RenameColumn(
                name: "PaymentDate",
                table: "order_payments",
                newName: "payment_date");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "order_payments",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "IsRefunded",
                table: "order_payments",
                newName: "is_refunded");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "order_payments",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "order_payments",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CardType",
                table: "order_payments",
                newName: "card_type");

            migrationBuilder.RenameColumn(
                name: "CardLastFourDigits",
                table: "order_payments",
                newName: "card_last_four_digits");

            migrationBuilder.RenameIndex(
                name: "IX_OrderPayment_TransactionId",
                table: "order_payments",
                newName: "IX_order_payments_transaction_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderPayment_PaymentDate",
                table: "order_payments",
                newName: "IX_order_payments_payment_date");

            migrationBuilder.RenameIndex(
                name: "IX_OrderPayment_OrderId",
                table: "order_payments",
                newName: "ix_order_payments_order_id");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "orders",
                newName: "type");

            migrationBuilder.RenameColumn(
                name: "Total",
                table: "orders",
                newName: "total");

            migrationBuilder.RenameColumn(
                name: "Tip",
                table: "orders",
                newName: "tip");

            migrationBuilder.RenameColumn(
                name: "Tax",
                table: "orders",
                newName: "tax");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "orders",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Priority",
                table: "orders",
                newName: "priority");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "orders",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Discount",
                table: "orders",
                newName: "discount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "orders",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserLimitAmount",
                table: "orders",
                newName: "user_limit_amount");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "orders",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "orders",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "orders",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TotalPaid",
                table: "orders",
                newName: "total_paid");

            migrationBuilder.RenameColumn(
                name: "TableNumber",
                table: "orders",
                newName: "table_number");

            migrationBuilder.RenameColumn(
                name: "SubTotal",
                table: "orders",
                newName: "sub_total");

            migrationBuilder.RenameColumn(
                name: "RemainingAmount",
                table: "orders",
                newName: "remaining_amount");

            migrationBuilder.RenameColumn(
                name: "PromoCode",
                table: "orders",
                newName: "promo_code");

            migrationBuilder.RenameColumn(
                name: "PaymentStatus",
                table: "orders",
                newName: "payment_status");

            migrationBuilder.RenameColumn(
                name: "OrderNumber",
                table: "orders",
                newName: "order_number");

            migrationBuilder.RenameColumn(
                name: "OrderDate",
                table: "orders",
                newName: "order_date");

            migrationBuilder.RenameColumn(
                name: "IsFocusOrder",
                table: "orders",
                newName: "is_focus_order");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "orders",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "HasUserLimitDiscount",
                table: "orders",
                newName: "has_user_limit_discount");

            migrationBuilder.RenameColumn(
                name: "FocusedBy",
                table: "orders",
                newName: "focused_by");

            migrationBuilder.RenameColumn(
                name: "FocusedAt",
                table: "orders",
                newName: "focused_at");

            migrationBuilder.RenameColumn(
                name: "FocusReason",
                table: "orders",
                newName: "focus_reason");

            migrationBuilder.RenameColumn(
                name: "EstimatedDeliveryTime",
                table: "orders",
                newName: "estimated_delivery_time");

            migrationBuilder.RenameColumn(
                name: "DiscountPercentage",
                table: "orders",
                newName: "discount_percentage");

            migrationBuilder.RenameColumn(
                name: "DeliveryFee",
                table: "orders",
                newName: "delivery_fee");

            migrationBuilder.RenameColumn(
                name: "DeliveryAddress",
                table: "orders",
                newName: "delivery_address");

            migrationBuilder.RenameColumn(
                name: "DeletedBy",
                table: "orders",
                newName: "deleted_by");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "orders",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CustomerPhone",
                table: "orders",
                newName: "customer_phone");

            migrationBuilder.RenameColumn(
                name: "CustomerName",
                table: "orders",
                newName: "customer_name");

            migrationBuilder.RenameColumn(
                name: "CustomerEmail",
                table: "orders",
                newName: "customer_email");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "orders",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "orders",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CancellationReason",
                table: "orders",
                newName: "cancellation_reason");

            migrationBuilder.RenameColumn(
                name: "ActualDeliveryTime",
                table: "orders",
                newName: "actual_delivery_time");

            migrationBuilder.RenameIndex(
                name: "IX_Order_UserId_OrderDate",
                table: "orders",
                newName: "IX_orders_user_id_order_date");

            migrationBuilder.RenameIndex(
                name: "IX_Order_UserId",
                table: "orders",
                newName: "ix_orders_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_Order_Status",
                table: "orders",
                newName: "IX_orders_status");

            migrationBuilder.RenameIndex(
                name: "IX_Order_OrderNumber",
                table: "orders",
                newName: "IX_orders_order_number");

            migrationBuilder.RenameIndex(
                name: "IX_Order_OrderDate",
                table: "orders",
                newName: "IX_orders_order_date");

            migrationBuilder.RenameIndex(
                name: "IX_Order_IsFocusOrder_Priority",
                table: "orders",
                newName: "IX_orders_is_focus_order_priority");

            migrationBuilder.RenameIndex(
                name: "IX_Order_IsFocusOrder",
                table: "orders",
                newName: "IX_orders_is_focus_order");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_status_histories",
                table: "OrderStatusHistory",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_items",
                table: "OrderItems",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_payments",
                table: "order_payments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_orders",
                table: "orders",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_payments_orders_order_id",
                table: "order_payments",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_menus_menu_id",
                table: "OrderItems",
                column: "menu_id",
                principalTable: "menus",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_orders_order_id",
                table: "OrderItems",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_products_product_id",
                table: "OrderItems",
                column: "product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_productvariations_product_variation_id",
                table: "OrderItems",
                column: "product_variation_id",
                principalTable: "product_variations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_asp_net_users_user_id",
                table: "orders",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_status_histories_orders_order_id",
                table: "OrderStatusHistory",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_order_payments_orders_order_id",
                table: "order_payments");

            migrationBuilder.DropForeignKey(
                name: "fk_order_items_menus_menu_id",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "fk_order_items_orders_order_id",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "fk_order_items_products_product_id",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "fk_order_items_productvariations_product_variation_id",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_asp_net_users_user_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_order_status_histories_orders_order_id",
                table: "OrderStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_status_histories",
                table: "OrderStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_items",
                table: "OrderItems");

            migrationBuilder.DropPrimaryKey(
                name: "pk_orders",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_payments",
                table: "order_payments");

            migrationBuilder.RenameTable(
                name: "orders",
                newName: "Order");

            migrationBuilder.RenameTable(
                name: "order_payments",
                newName: "OrderPayment");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "OrderStatusHistory",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderStatusHistory",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "OrderStatusHistory",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderStatusHistory",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "to_status",
                table: "OrderStatusHistory",
                newName: "ToStatus");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "OrderStatusHistory",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "from_status",
                table: "OrderStatusHistory",
                newName: "FromStatus");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "OrderStatusHistory",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderStatusHistory",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "changed_by",
                table: "OrderStatusHistory",
                newName: "ChangedBy");

            migrationBuilder.RenameColumn(
                name: "changed_at",
                table: "OrderStatusHistory",
                newName: "ChangedAt");

            migrationBuilder.RenameIndex(
                name: "IX_OrderStatusHistory_changed_at",
                table: "OrderStatusHistory",
                newName: "IX_OrderStatusHistory_ChangedAt");

            migrationBuilder.RenameIndex(
                name: "ix_order_status_histories_order_id",
                table: "OrderStatusHistory",
                newName: "IX_OrderStatusHistory_OrderId");

            migrationBuilder.RenameColumn(
                name: "quantity",
                table: "OrderItems",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderItems",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "variation_name",
                table: "OrderItems",
                newName: "VariationName");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "OrderItems",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderItems",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "unit_price",
                table: "OrderItems",
                newName: "UnitPrice");

            migrationBuilder.RenameColumn(
                name: "special_instructions",
                table: "OrderItems",
                newName: "SpecialInstructions");

            migrationBuilder.RenameColumn(
                name: "product_variation_id",
                table: "OrderItems",
                newName: "ProductVariationId");

            migrationBuilder.RenameColumn(
                name: "product_name",
                table: "OrderItems",
                newName: "ProductName");

            migrationBuilder.RenameColumn(
                name: "product_id",
                table: "OrderItems",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "OrderItems",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "menu_id",
                table: "OrderItems",
                newName: "MenuId");

            migrationBuilder.RenameColumn(
                name: "item_total",
                table: "OrderItems",
                newName: "ItemTotal");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "OrderItems",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderItems",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_order_items_product_variation_id",
                table: "OrderItems",
                newName: "IX_OrderItems_ProductVariationId");

            migrationBuilder.RenameIndex(
                name: "ix_order_items_product_id",
                table: "OrderItems",
                newName: "IX_OrderItems_ProductId");

            migrationBuilder.RenameIndex(
                name: "ix_order_items_order_id",
                table: "OrderItems",
                newName: "IX_OrderItems_OrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_items_menu_id",
                table: "OrderItems",
                newName: "IX_OrderItems_MenuId");

            migrationBuilder.RenameColumn(
                name: "type",
                table: "Order",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "total",
                table: "Order",
                newName: "Total");

            migrationBuilder.RenameColumn(
                name: "tip",
                table: "Order",
                newName: "Tip");

            migrationBuilder.RenameColumn(
                name: "tax",
                table: "Order",
                newName: "Tax");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Order",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "priority",
                table: "Order",
                newName: "Priority");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "Order",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "discount",
                table: "Order",
                newName: "Discount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Order",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_limit_amount",
                table: "Order",
                newName: "UserLimitAmount");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "Order",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "Order",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Order",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "total_paid",
                table: "Order",
                newName: "TotalPaid");

            migrationBuilder.RenameColumn(
                name: "table_number",
                table: "Order",
                newName: "TableNumber");

            migrationBuilder.RenameColumn(
                name: "sub_total",
                table: "Order",
                newName: "SubTotal");

            migrationBuilder.RenameColumn(
                name: "remaining_amount",
                table: "Order",
                newName: "RemainingAmount");

            migrationBuilder.RenameColumn(
                name: "promo_code",
                table: "Order",
                newName: "PromoCode");

            migrationBuilder.RenameColumn(
                name: "payment_status",
                table: "Order",
                newName: "PaymentStatus");

            migrationBuilder.RenameColumn(
                name: "order_number",
                table: "Order",
                newName: "OrderNumber");

            migrationBuilder.RenameColumn(
                name: "order_date",
                table: "Order",
                newName: "OrderDate");

            migrationBuilder.RenameColumn(
                name: "is_focus_order",
                table: "Order",
                newName: "IsFocusOrder");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "Order",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "has_user_limit_discount",
                table: "Order",
                newName: "HasUserLimitDiscount");

            migrationBuilder.RenameColumn(
                name: "focused_by",
                table: "Order",
                newName: "FocusedBy");

            migrationBuilder.RenameColumn(
                name: "focused_at",
                table: "Order",
                newName: "FocusedAt");

            migrationBuilder.RenameColumn(
                name: "focus_reason",
                table: "Order",
                newName: "FocusReason");

            migrationBuilder.RenameColumn(
                name: "estimated_delivery_time",
                table: "Order",
                newName: "EstimatedDeliveryTime");

            migrationBuilder.RenameColumn(
                name: "discount_percentage",
                table: "Order",
                newName: "DiscountPercentage");

            migrationBuilder.RenameColumn(
                name: "delivery_fee",
                table: "Order",
                newName: "DeliveryFee");

            migrationBuilder.RenameColumn(
                name: "delivery_address",
                table: "Order",
                newName: "DeliveryAddress");

            migrationBuilder.RenameColumn(
                name: "deleted_by",
                table: "Order",
                newName: "DeletedBy");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "Order",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "customer_phone",
                table: "Order",
                newName: "CustomerPhone");

            migrationBuilder.RenameColumn(
                name: "customer_name",
                table: "Order",
                newName: "CustomerName");

            migrationBuilder.RenameColumn(
                name: "customer_email",
                table: "Order",
                newName: "CustomerEmail");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "Order",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Order",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "cancellation_reason",
                table: "Order",
                newName: "CancellationReason");

            migrationBuilder.RenameColumn(
                name: "actual_delivery_time",
                table: "Order",
                newName: "ActualDeliveryTime");

            migrationBuilder.RenameIndex(
                name: "IX_orders_user_id_order_date",
                table: "Order",
                newName: "IX_Order_UserId_OrderDate");

            migrationBuilder.RenameIndex(
                name: "ix_orders_user_id",
                table: "Order",
                newName: "IX_Order_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_orders_status",
                table: "Order",
                newName: "IX_Order_Status");

            migrationBuilder.RenameIndex(
                name: "IX_orders_order_number",
                table: "Order",
                newName: "IX_Order_OrderNumber");

            migrationBuilder.RenameIndex(
                name: "IX_orders_order_date",
                table: "Order",
                newName: "IX_Order_OrderDate");

            migrationBuilder.RenameIndex(
                name: "IX_orders_is_focus_order_priority",
                table: "Order",
                newName: "IX_Order_IsFocusOrder_Priority");

            migrationBuilder.RenameIndex(
                name: "IX_orders_is_focus_order",
                table: "Order",
                newName: "IX_Order_IsFocusOrder");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "OrderPayment",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "OrderPayment",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderPayment",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "OrderPayment",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderPayment",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "transaction_id",
                table: "OrderPayment",
                newName: "TransactionId");

            migrationBuilder.RenameColumn(
                name: "refunded_amount",
                table: "OrderPayment",
                newName: "RefundedAmount");

            migrationBuilder.RenameColumn(
                name: "refund_reason",
                table: "OrderPayment",
                newName: "RefundReason");

            migrationBuilder.RenameColumn(
                name: "refund_date",
                table: "OrderPayment",
                newName: "RefundDate");

            migrationBuilder.RenameColumn(
                name: "reference_number",
                table: "OrderPayment",
                newName: "ReferenceNumber");

            migrationBuilder.RenameColumn(
                name: "payment_notes",
                table: "OrderPayment",
                newName: "PaymentNotes");

            migrationBuilder.RenameColumn(
                name: "payment_method",
                table: "OrderPayment",
                newName: "PaymentMethod");

            migrationBuilder.RenameColumn(
                name: "payment_gateway",
                table: "OrderPayment",
                newName: "PaymentGateway");

            migrationBuilder.RenameColumn(
                name: "payment_date",
                table: "OrderPayment",
                newName: "PaymentDate");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "OrderPayment",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "is_refunded",
                table: "OrderPayment",
                newName: "IsRefunded");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "OrderPayment",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderPayment",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "card_type",
                table: "OrderPayment",
                newName: "CardType");

            migrationBuilder.RenameColumn(
                name: "card_last_four_digits",
                table: "OrderPayment",
                newName: "CardLastFourDigits");

            migrationBuilder.RenameIndex(
                name: "IX_order_payments_transaction_id",
                table: "OrderPayment",
                newName: "IX_OrderPayment_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_order_payments_payment_date",
                table: "OrderPayment",
                newName: "IX_OrderPayment_PaymentDate");

            migrationBuilder.RenameIndex(
                name: "ix_order_payments_order_id",
                table: "OrderPayment",
                newName: "IX_OrderPayment_OrderId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderStatusHistory",
                table: "OrderStatusHistory",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderItems",
                table: "OrderItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Order",
                table: "Order",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderPayment",
                table: "OrderPayment",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Order_Users_UserId",
                table: "Order",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Order_OrderId",
                table: "OrderItems",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_menus_MenuId",
                table: "OrderItems",
                column: "MenuId",
                principalTable: "menus",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_product_variations_ProductVariationId",
                table: "OrderItems",
                column: "ProductVariationId",
                principalTable: "product_variations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayment_Order_OrderId",
                table: "OrderPayment",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusHistory_Order_OrderId",
                table: "OrderStatusHistory",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
