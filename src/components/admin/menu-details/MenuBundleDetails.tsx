import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import styles from './MenuBundleDetails.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import ProductInformation from '@/components/admin/product-details/ProductInformation';
import MultilingualContentEditor from '@/components/admin/product-details/MultilingualContentEditor';
import MenuScheduleEditor from '@/components/admin/menu-editor/MenuScheduleEditor';
import MenuSectionEditor from '@/components/admin/menu-editor/MenuSectionEditor';
import ProductCard from './ProductCard';
import { updateMenuBundle } from '@/services/menuService';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';

interface MenuBundleDetailsProps {
  product: ProductDetails;
  onUpdated: () => void;
}

const MenuBundleDetails: React.FC<MenuBundleDetailsProps> = ({ product, onUpdated }) => {
  const { t } = useTranslation();
  const [selectedImageIndex, setSelectedImageIndex] = useState(0);
  const [scheduleOpen, setScheduleOpen] = useState(false);
  const [sectionsOpen, setSectionsOpen] = useState(true);
  const [contentOpen, setContentOpen] = useState(false);
  const [infoOpen, setInfoOpen] = useState(false);

  const handleMenuDefinitionUpdate = async (updates: Partial<typeof product.menuDefinition>) => {
    if (!product.menuDefinition) return;
    try {
      const updatedMenuDefinition = { ...product.menuDefinition, ...updates };

      // Clean up temporary IDs from sections and items
      const cleanedSections = updatedMenuDefinition.sections?.map(s => {
        const sectionData: any = {
          name: s.name,
          description: s.description,
          displayOrder: s.displayOrder,
          isRequired: s.isRequired,
          minSelection: s.minSelection,
          maxSelection: s.maxSelection,
          items: s.items.map(i => {
            const itemData: any = {
              productId: i.productId,
              additionalPrice: i.additionalPrice,
              displayOrder: i.displayOrder,
              isDefault: i.isDefault
            };
            // Only include id if it's not temporary
            if (i.id && !i.id.startsWith('temp-')) {
              itemData.id = i.id;
            }
            return itemData;
          })
        };
        // Only include section id if it's not temporary
        if (s.id && !s.id.startsWith('temp-')) {
          sectionData.id = s.id;
        }
        return sectionData;
      }) || [];

      const payload = {
        id: product.id,
        name: product.name,
        description: product.description,
        basePrice: product.basePrice,
        isActive: product.isActive,
        isAvailable: product.isAvailable,
        isSpecial: product.isSpecial,
        preparationTimeMinutes: product.preparationTimeMinutes,
        displayOrder: product.displayOrder,
        content: product.content || {},
        menuDefinition: {
          ...updatedMenuDefinition,
          sections: cleanedSections
        }
      };

      await updateMenuBundle(product.id, payload);
      onUpdated();
    } catch (error) {
      console.error('Error updating menu bundle:', error);
    }
  };

  const images = product.images || [];
  const hasImages = images.length > 0;
  const selectedImage = hasImages ? images[selectedImageIndex] : null;

  const getDayName = (day: string) => {
    const days: Record<string, string> = {
      monday: t('monday'),
      tuesday: t('tuesday'),
      wednesday: t('wednesday'),
      thursday: t('thursday'),
      friday: t('friday'),
      saturday: t('saturday'),
      sunday: t('sunday')
    };
    return days[day] || day;
  };

  const isDayAvailable = (day: string) => {
    if (!product.menuDefinition) return false;
    const key = `available${day.charAt(0).toUpperCase() + day.slice(1)}` as keyof typeof product.menuDefinition;
    return product.menuDefinition[key] as boolean;
  };

  return (
    <div className={styles.menuBundleContainer}>
      {/* Hero Section */}
      <div className={styles.heroSection}>
        <div className={styles.heroContent}>
          <h1 className={styles.heroTitle}>{product.name}</h1>
          {product.description && (
            <p className={styles.heroDescription}>{product.description}</p>
          )}
          <div className={styles.heroBadges}>
            <div className={styles.priceTag}>CHF {product.basePrice.toFixed(2)}</div>
            <span className={`${styles.statusBadge} ${product.isActive ? styles.statusActive : styles.statusInactive}`}>
              {product.isActive ? t('active') : t('inactive')}
            </span>
            {product.isAvailable && (
              <span className={`${styles.statusBadge} ${styles.statusActive}`}>
                {t('available')}
              </span>
            )}
            {product.isSpecial && (
              <span className={`${styles.statusBadge} ${styles.statusSpecial}`}>
                {t('special')}
              </span>
            )}
          </div>
        </div>
      </div>

      {/* Image Carousel */}
      {hasImages && (
        <div className={styles.imageCarouselSection}>
          <div className={styles.imageCarousel}>
            <img
              src={selectedImage?.url}
              alt={selectedImage?.altText || product.name}
              className={styles.carouselImage}
            />
          </div>
          {images.length > 1 && (
            <div className={styles.carouselThumbnails}>
              {images.map((img, idx) => (
                <img
                  key={idx}
                  src={img.url}
                  alt={img.altText || `${product.name} ${idx + 1}`}
                  className={`${styles.carouselThumbnail} ${idx === selectedImageIndex ? styles.active : ''}`}
                  onClick={() => setSelectedImageIndex(idx)}
                />
              ))}
            </div>
          )}
        </div>
      )}

      {/* Menu Schedule */}
      {product.menuDefinition && (
        <div className={styles.collapsibleSection}>
          <div
            className={styles.collapsibleHeader}
            onClick={() => setScheduleOpen(!scheduleOpen)}
          >
            <h2 className={styles.collapsibleTitle}>
              📅 {t('menu_availability')}
            </h2>
            <span className={`${styles.collapsibleIcon} ${scheduleOpen ? styles.open : ''}`}>
              ▼
            </span>
          </div>
          <div className={`${styles.collapsibleContent} ${scheduleOpen ? styles.open : ''}`}>
            {product.menuDefinition.isAlwaysAvailable ? (
              <div className={styles.scheduleTime}>
                ✅ {t('always_available')}
              </div>
            ) : (
              <>
                {(product.menuDefinition.startTime || product.menuDefinition.endTime) && (
                  <div className={styles.scheduleTime}>
                    🕐 {product.menuDefinition.startTime || '00:00'} - {product.menuDefinition.endTime || '23:59'}
                  </div>
                )}
                <div className={styles.scheduleGrid}>
                  {['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'].map(day => (
                    <div
                      key={day}
                      className={`${styles.scheduleDay} ${isDayAvailable(day) ? styles.available : styles.unavailable}`}
                    >
                      {getDayName(day)}
                    </div>
                  ))}
                </div>
              </>
            )}
            <div style={{ marginTop: '1.5rem' }}>
              <MenuScheduleEditor
                menuDefinition={product.menuDefinition}
                onChange={(menuDefinition) => handleMenuDefinitionUpdate(menuDefinition)}
              />
            </div>
          </div>
        </div>
      )}

      {/* Menu Sections */}
      {product.menuDefinition && product.menuDefinition.sections && (
        <div className={styles.collapsibleSection}>
          <div
            className={styles.collapsibleHeader}
            onClick={() => setSectionsOpen(!sectionsOpen)}
          >
            <h2 className={styles.collapsibleTitle}>
              🍽️ {t('menu_sections')}
            </h2>
            <span className={`${styles.collapsibleIcon} ${sectionsOpen ? styles.open : ''}`}>
              ▼
            </span>
          </div>
          <div className={`${styles.collapsibleContent} ${sectionsOpen ? styles.open : ''}`}>
            <div className={styles.menuSectionsContainer}>
              {product.menuDefinition.sections.map((section) => (
                <div key={section.id} className={styles.menuSection}>
                  <div className={styles.menuSectionHeader}>
                    <h3 className={styles.menuSectionTitle}>{section.name}</h3>
                    {section.description && (
                      <p className={styles.menuSectionDescription}>{section.description}</p>
                    )}
                    <div className={styles.menuSectionMeta}>
                      {section.isRequired && (
                        <span className={`${styles.metaBadge} ${styles.required}`}>
                          ⚠️ {t('required')}
                        </span>
                      )}
                      <span className={styles.metaBadge}>
                        {t('select')}: {section.minSelection} - {section.maxSelection}
                      </span>
                      <span className={styles.metaBadge}>
                        {section.items.length} {t('items')}
                      </span>
                    </div>
                  </div>
                  <div className={styles.productsGrid}>
                    {section.items.map((item) => (
                      <ProductCard key={item.id} item={item} />
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <div style={{ marginTop: '1.5rem', padding: '1rem', background: 'var(--secondary-color-light)', borderRadius: '8px' }}>
              <h4 style={{ marginTop: 0 }}>{t('edit_sections')}</h4>
              <MenuSectionEditor
                sections={product.menuDefinition.sections}
                onChange={(sections) => handleMenuDefinitionUpdate({ sections })}
              />
            </div>
          </div>
        </div>
      )}

      {/* Multilingual Content */}
      <div className={styles.collapsibleSection}>
        <div
          className={styles.collapsibleHeader}
          onClick={() => setContentOpen(!contentOpen)}
        >
          <h2 className={styles.collapsibleTitle}>
            🌐 {t('multilingual_content')}
          </h2>
          <span className={`${styles.collapsibleIcon} ${contentOpen ? styles.open : ''}`}>
            ▼
          </span>
        </div>
        <div className={`${styles.collapsibleContent} ${contentOpen ? styles.open : ''}`}>
          <MultilingualContentEditor product={product} onUpdated={onUpdated} />
        </div>
      </div>

      {/* Product Information (Admin) */}
      <div className={styles.collapsibleSection}>
        <div
          className={styles.collapsibleHeader}
          onClick={() => setInfoOpen(!infoOpen)}
        >
          <h2 className={styles.collapsibleTitle}>
            ⚙️ {t('product_information')}
          </h2>
          <span className={`${styles.collapsibleIcon} ${infoOpen ? styles.open : ''}`}>
            ▼
          </span>
        </div>
        <div className={`${styles.collapsibleContent} ${infoOpen ? styles.open : ''}`}>
          <ProductInformation product={product} onUpdated={onUpdated} />
        </div>
      </div>
    </div>
  );
};

export default MenuBundleDetails;
