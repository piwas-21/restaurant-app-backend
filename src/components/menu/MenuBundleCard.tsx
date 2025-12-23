import React from 'react';
import { useTranslation } from 'react-i18next';
import type { MenuBundleItem } from '@/types/menu';
import type { LanguageCode } from '@/components/LanguageSwitcher';
import MenuItemImage from './MenuItemImage';
import MenuItemActions from './MenuItemActions';
import styles from './MenuBundleCard.module.css';

interface MenuBundleCardProps {
  bundle: MenuBundleItem;
  currentLanguage: LanguageCode;
  onAdd: (bundle: MenuBundleItem) => void;
  onDetails: (bundle: MenuBundleItem) => void;
}

const MenuBundleCard: React.FC<MenuBundleCardProps> = ({
  bundle,
  currentLanguage,
  onAdd,
  onDetails,
}) => {
  const { t } = useTranslation();

  const bundleName =
    bundle.content?.[currentLanguage]?.name ||
    bundle.content?.en?.name ||
    bundle.name;

  const bundleDescription =
    bundle.content?.[currentLanguage]?.description ||
    bundle.content?.en?.description ||
    bundle.description ||
    '';

  const primaryImage =
    bundle.images && bundle.images.length > 0
      ? bundle.images[0].url
      : '/images/placeholder-app.png';

  // Get default items to show in the card
  const defaultItems =
    bundle.menuDefinition?.sections?.flatMap((section) =>
      section.items
        .filter((item) => item.isDefault)
        .map((item) => item.productName)
    ) || [];

  const handleAdd = (e?: React.MouseEvent) => {
    e?.stopPropagation();
    onAdd(bundle);
  };

  const handleDetails = (e?: React.MouseEvent) => {
    e?.stopPropagation();
    if (onDetails) {
      onDetails(bundle);
    }
  };

  return (
    <div className={styles.bundleCard} onClick={handleDetails}>
      {bundle.isSpecial && (
        <div className={styles.specialBadge}>{t('special')}</div>
      )}

      <div className={styles.imageContainer}>
        <MenuItemImage
          imageUrl={primaryImage}
          alt={bundleName}
          onClick={handleDetails}
        />
      </div>

      <div className={styles.content}>
        <div className={styles.header}>
          <h3 className={styles.title}>{bundleName}</h3>
          {bundleDescription && (
            <p className={styles.description}>{bundleDescription}</p>
          )}
        </div>

        <div className={styles.includesSection}>
          {/* <span className={styles.includesLabel}>{t('bundle_includes')}:</span> */}
          <div className={styles.includesList}>
            <span className={styles.includeItem}>
              {defaultItems.join(' + ')}
            </span>
          </div>
        </div>

        <div className={styles.footer}>
          <div className={styles.price}>CHF {bundle.basePrice.toFixed(2)}</div>
          <MenuItemActions
            onAdd={handleAdd}
            onFeedback={() => {}} // Feedback not implemented for bundles yet
            addLabel={t('add_to_order')}
            addAria={t('add_bundle_to_cart')}
            feedbackLabel=""
            feedbackAria=""
            onDetails={handleDetails}
            detailsLabel={t('details')}
          />
        </div>
      </div>
    </div>
  );
};

export default MenuBundleCard;
